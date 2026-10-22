import { useEffect, useState } from 'preact/hooks';
import type { CSSProperties } from 'preact';
import { request } from '../lib/bridge';
import { formatBytes, formatCount, formatDate, loaderName, projectTypeName } from '../lib/format';
import {
  cancelContentJob,
  contentJobs,
  errorMessage,
  registerContentJob,
  showToast
} from '../lib/store';
import type { InstanceCard, ModrinthHit, ModrinthVersionInfo } from '../lib/types';
import { Icon } from './Icon';
import { Button, ProgressBar, Sheet } from './ui';

interface ContentDetailSheetProps {
  open: boolean;
  hit: ModrinthHit | null;
  instance: InstanceCard | null;
  source?: 'modrinth' | 'curseforge';
  onClose: () => void;
}

const TYPE_LABEL: Record<string, string> = {
  release: '正式版',
  beta: '测试版',
  alpha: '开发版'
};

export function ContentDetailSheet({ open, hit, instance, source = 'modrinth', onClose }: ContentDetailSheetProps) {
  const [versions, setVersions] = useState<ModrinthVersionInfo[]>([]);
  const [selectedId, setSelectedId] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [requestId, setRequestId] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);

  useEffect(() => {
    if (!open || !hit || !instance) return;
    let alive = true;
    setLoading(true);
    setError(null);
    setVersions([]);
    setSelectedId('');
    setRequestId(null);
    request<ModrinthVersionInfo[]>(
      'modrinth.versions',
      {
        instanceId: instance.instance.id,
        projectId: hit.projectId,
        projectType: hit.projectType,
        source
      },
      45000
    )
      .then((list) => {
        if (!alive) return;
        setVersions(list ?? []);
        setSelectedId(list?.[0]?.id ?? '');
      })
      .catch((reason) => {
        if (!alive) return;
        setError(errorMessage(reason));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [open, hit?.projectId, hit?.projectType, instance?.instance.id, source]);

  useEffect(() => {
    if (!open) setRequestId(null);
  }, [open]);

  if (!hit || !instance) return null;

  const selected = versions.find((version) => version.id === selectedId) ?? versions[0] ?? null;
  const job = requestId ? contentJobs.value[requestId] : null;
  const busy = job?.state === 'running' || starting;
  const vanillaBlocked = hit.projectType === 'mod' && instance.instance.loader === 'vanilla';
  const installed = hit.installed;

  const install = async () => {
    if (!selected || vanillaBlocked) return;
    setStarting(true);
    try {
      const result = await request<{ accepted: boolean; requestId: string }>(
        'modrinth.install',
        {
          instanceId: instance.instance.id,
          projectId: hit.projectId,
          versionId: selected.id,
          projectType: hit.projectType,
          source
        },
        30000
      );
      if (result?.requestId) {
        setRequestId(result.requestId);
        void registerContentJob(result.requestId, {
          projectId: hit.projectId,
          title: hit.title,
          instanceId: instance.instance.id
        });
      }
    } catch (reason) {
      showToast('没有开始下载', 'error', errorMessage(reason));
    } finally {
      setStarting(false);
    }
  };

  const pct = job?.progress && job.progress.totalBytes > 0
    ? job.progress.receivedBytes / job.progress.totalBytes
    : 0;
  const finished = job?.state === 'done';
  const failed = job?.state === 'failed';
  const cancelled = job?.state === 'cancelled';

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={hit.title}
      eyebrow={`${projectTypeName(hit.projectType)} · ${hit.author}`}
      subtitle={hit.description}
      titleId="content-detail-title"
      width="normal"
      footerNote={
        finished
          ? '文件已经放进实例，下次启动时生效。'
          : `来自 Modrinth · 版权归作者所有。`
      }
      footer={
        finished ? (
          <Button variant="primary" onClick={onClose}>
            完成
          </Button>
        ) : failed || cancelled ? (
          <>
            <Button variant="quiet" onClick={onClose}>
              关闭
            </Button>
            <Button variant="primary" icon="refresh" onClick={() => void install()}>
              再试一次
            </Button>
          </>
        ) : busy ? (
          <Button variant="danger" onClick={() => requestId && void cancelContentJob(requestId)}>
            取消下载
          </Button>
        ) : (
          <>
            <Button variant="quiet" onClick={onClose}>
              关闭
            </Button>
            <Button
              variant="primary"
              icon="download"
              loading={starting}
              disabled={!selected || vanillaBlocked}
              onClick={() => void install()}
            >
              {installed ? '装上这个版本' : '放进实例'}
            </Button>
          </>
        )
      }
    >
      <div class="content-hero">
        <div class="content-icon content-icon--lg" style={iconStyle(hit)}>
          {hit.iconUrl ? (
            <img src={hit.iconUrl} alt="" loading="lazy" referrerpolicy="no-referrer" />
          ) : (
            <span>{hit.title.slice(0, 1).toUpperCase()}</span>
          )}
        </div>
        <div class="content-hero__body">
          <div class="content-hero__chips">
            <span class="chip">{projectTypeName(hit.projectType)}</span>
            {hit.categories.map((category) => (
              <span class="chip chip--bare" key={category}>
                {category}
              </span>
            ))}
            {installed ? <span class="chip chip--moss">已在实例中</span> : null}
          </div>
          <div class="content-hero__stats">
            <span>
              <b class="mono">{formatCount(hit.downloads)}</b> 次下载
            </span>
            <span class="dot-sep">·</span>
            <span>
              <b class="mono">{formatCount(hit.follows)}</b> 人收藏
            </span>
            <span class="dot-sep">·</span>
            <span>适配 {instance.instance.gameVersion}</span>
          </div>
        </div>
      </div>

      <div class="content-instance-line">
        <Icon name="layers" size={13} />
        将被放进 <b>{instance.instance.displayName}</b>
        <span class="faint">
          {instance.instance.gameVersion} · {loaderName(instance.instance.loader)}
        </span>
      </div>

      {vanillaBlocked ? (
        <div class="sheet-note sheet-note--warn" style={{ marginTop: 16 }}>
          这个实例是原版，没有模组加载器。可以新建一个 Fabric 实例，或者切换到资源包与光影。
        </div>
      ) : null}

      <section class="sheet-section">
        <div class="sheet-section__head">
          <span class="sheet-section__title">选择版本</span>
          {versions.length > 0 ? <span class="mono faint">{versions.length} 个可用</span> : null}
        </div>
        {loading ? (
          <div class="content-detail-loading">
            <span class="btn-spinner" /> 正在向 Modrinth 询问可用版本…
          </div>
        ) : error ? (
          <div class="sheet-note sheet-note--bad">{error}</div>
        ) : versions.length === 0 ? (
          <div class="sheet-note">
            没有找到适配 {instance.instance.gameVersion} / {loaderName(instance.instance.loader)} 的版本。
          </div>
        ) : (
          <>
            <div class="select-wrap">
              <select
                class="select"
                value={selected?.id ?? ''}
                onChange={(event) => setSelectedId((event.target as HTMLSelectElement).value)}
                aria-label="选择内容版本"
              >
                {versions.map((version) => (
                  <option key={version.id} value={version.id}>
                    {version.versionNumber} · {TYPE_LABEL[version.versionType] ?? version.versionType} ·{' '}
                    {formatDate(version.datePublished)}
                  </option>
                ))}
              </select>
              <Icon name="chevronDown" size={13} class="select-wrap__chevron" />
            </div>
            {selected ? (
              <>
                <div class="content-version-meta">
                  <span class="chip chip--copper">{TYPE_LABEL[selected.versionType] ?? selected.versionType}</span>
                  <span class="chip">{selected.loaders.join(' / ') || '通用'}</span>
                  <span class="chip">{selected.gameVersions.slice(0, 3).join(', ')}</span>
                  <span class="mono faint">{formatBytes(selected.file?.size)}</span>
                </div>
                {selected.dependencies.some((dependency) => dependency.dependencyType === 'required') ? (
                  <div class="content-deps">
                    <span class="content-deps__label">需要的前置</span>
                    <div class="diagnosis__chips">
                      {selected.dependencies
                        .filter((dependency) => dependency.dependencyType === 'required')
                        .map((dependency) => (
                          <span class="chip chip--moss" key={dependency.projectId ?? dependency.versionId}>
                            {dependency.projectId ?? dependency.versionId}
                          </span>
                        ))}
                    </div>
                    <span class="content-deps__note">安装时会自动把缺少的前置一并带上。</span>
                  </div>
                ) : null}
                {selected.changelog.length > 0 ? (
                  <div class="content-changelog">
                    <div class="content-changelog__label">这一版</div>
                    {selected.changelog.slice(0, 4).map((line, index) => (
                      <p key={index}>{line}</p>
                    ))}
                  </div>
                ) : null}
              </>
            ) : null}
          </>
        )}
      </section>

      {requestId ? (
        <section class="sheet-section">
          <div class="sheet-section__head">
            <span class="sheet-section__title">
              {finished ? '已经放好' : failed ? '没有装好' : cancelled ? '已取消' : '正在装'}
            </span>
            {job?.progress?.projectTitle ? <span class="mono faint">{job.progress.projectTitle}</span> : null}
          </div>
          {finished ? (
            <div class="content-result">
              <span class="content-result__mark">
                <Icon name="check" size={15} />
              </span>
              <div>
                <div>{hit.title} 已放进 {instance.instance.displayName}。</div>
                <div class="faint">如果刚刚装着游戏，重启游戏后才会加载。</div>
              </div>
            </div>
          ) : failed ? (
            <div class="sheet-note sheet-note--bad">{job?.message || '下载中断了，可以再试一次。'}</div>
          ) : cancelled ? (
            <div class="sheet-note">已取消，没有留下损坏的文件。</div>
          ) : (
            <>
              <ProgressBar value={pct} indeterminate={!job?.progress || job.progress.totalBytes <= 0} />
              <div class="progress-meta">
                <span>{job?.progress?.message ?? '正在请求下载地址'}</span>
                <span>
                  {job?.progress ? `${formatBytes(job.progress.receivedBytes)} / ${formatBytes(job.progress.totalBytes)}` : ''}
                </span>
              </div>
            </>
          )}
        </section>
      ) : null}

      {installed ? (
        <div class="sheet-note" style={{ marginTop: 18 }}>
          这个项目已经存在于实例中。装另一个版本会替换原来的文件，存档不受影响。
        </div>
      ) : null}
    </Sheet>
  );
}

function iconStyle(hit: ModrinthHit): CSSProperties | undefined {
  const color = hit.color;
  if (!color) return undefined;
  const r = (color >> 16) & 255;
  const g = (color >> 8) & 255;
  const b = color & 255;
  return { '--content-accent': `rgb(${r} ${g} ${b})` } as CSSProperties;
}
