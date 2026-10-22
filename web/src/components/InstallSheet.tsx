import { useEffect, useRef, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import { formatBytes, formatDate, formatSpeed, loaderName } from '../lib/format';
import { navigate } from '../lib/router';
import {
  cancelInstallation,
  errorMessage,
  closeInstallSheet,
  instances,
  installJob,
  installSheetOpen,
  launchGame,
  memoryInfo,
  settings,
  showToast,
  startInstallation,
  updateSettings
} from '../lib/store';
import type { CatalogPayload, RemoteVersion, VersionInspection } from '../lib/types';
import { Icon } from './Icon';
import { LoaderTile } from './LoaderTile';
import { Button, Field, ProgressBar, Segmented, Sheet } from './ui';

type Channel = 'release' | 'snapshot';
type Loader = 'vanilla' | 'fabric' | 'quilt' | 'forge' | 'neoforge';

const LOADER_META: Array<{ id: Loader; label: string; note: string }> = [
  { id: 'vanilla', label: '原版', note: '不安装加载器' },
  { id: 'fabric', label: 'Fabric', note: '轻量，模组生态最大' },
  { id: 'quilt', label: 'Quilt', note: '兼容 Fabric 模组' },
  { id: 'forge', label: 'Forge', note: '老牌模组加载器' },
  { id: 'neoforge', label: 'NeoForge', note: 'Forge 的现代分支' }
];

export function InstallSheet() {
  const open = installSheetOpen.value;
  const job = installJob.value;
  const [channel, setChannel] = useState<Channel>('release');
  const [search, setSearch] = useState('');
  const [catalog, setCatalog] = useState<CatalogPayload | null>(null);
  const [catalogLoading, setCatalogLoading] = useState(false);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [selectedVersion, setSelectedVersion] = useState('');
  const [inspection, setInspection] = useState<VersionInspection | null>(null);
  const [inspecting, setInspecting] = useState(false);
  const [inspectError, setInspectError] = useState<string | null>(null);
  const [loader, setLoader] = useState<Loader>('vanilla');
  const [loaderVersion, setLoaderVersion] = useState<string | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [visibleCount, setVisibleCount] = useState(48);
  const [starting, setStarting] = useState(false);
  const inspectSeq = useRef(0);
  const catalogLoaded = useRef(false);

  const loadCatalog = async (force = false) => {
    if (catalogLoaded.current && !force) return;
    setCatalogLoading(true);
    setCatalogError(null);
    try {
      const data = await request<CatalogPayload>('catalog.list', {}, 45000);
      catalogLoaded.current = true;
      setCatalog(data);
      if (!selectedVersion) setSelectedVersion(data.latest.release);
    } catch (error) {
      catalogLoaded.current = false;
      setCatalogError(errorMessage(error));
    } finally {
      setCatalogLoading(false);
    }
  };

  useEffect(() => {
    if (!open) return;
    const previous = installJob.peek();
    if (previous && previous.state !== 'running') installJob.value = null;
    setStarting(false);
    void loadCatalog();
  }, [open]);

  useEffect(() => {
    if (!open || !selectedVersion) return;
    const seq = ++inspectSeq.current;
    setInspecting(true);
    setInspectError(null);
    request<VersionInspection>('version.inspect', { gameVersion: selectedVersion }, 60000)
      .then((data) => {
        if (seq !== inspectSeq.current) return;
        setInspection(data);
      })
      .catch((error) => {
        if (seq !== inspectSeq.current) return;
        setInspection(null);
        setInspectError(errorMessage(error));
      })
      .finally(() => {
        if (seq === inspectSeq.current) setInspecting(false);
      });
  }, [open, selectedVersion]);

  useEffect(() => {
    setVisibleCount(48);
  }, [channel, search]);

  useEffect(() => {
    if (!inspection) return;
    const info = inspection.loaders.find((item) => item.type === loader);
    if (loader !== 'vanilla' && (!info || !info.available || info.versions.length === 0)) {
      setLoader('vanilla');
      setLoaderVersion(null);
      return;
    }
    if (loader !== 'vanilla' && info) {
      if (!loaderVersion || !info.versions.some((item) => item.version === loaderVersion)) {
        const preferred = info.versions.find((item) => item.stable) ?? info.versions[0];
        setLoaderVersion(preferred?.version ?? null);
      }
    }
  }, [inspection, loader, loaderVersion]);

  const installedSet = new Set(catalog?.installedIds ?? []);
  const knownVersions = catalog?.versions ?? [];
  const versions = knownVersions.filter((entry) => {
    const wanted = channel === 'release' ? entry.type === 'release' : entry.type !== 'release';
    if (!wanted) return false;
    const text = search.trim().toLowerCase();
    return !text || entry.id.toLowerCase().includes(text);
  });
  const shown = versions.slice(0, visibleCount);
  const hasMore = versions.length > shown.length;
  const currentInstalled = instances.value.some(
    (card) => card.instance.gameVersion === selectedVersion && card.instance.loader === loader
  );

  const changeChannel = (next: Channel) => {
    setChannel(next);
    if (!catalog) return;
    const preferred = next === 'release' ? catalog.latest.release : catalog.latest.snapshot;
    setSelectedVersion(preferred);
  };

  const chooseLoader = (next: Loader) => {
    setLoader(next);
    if (next === 'vanilla') {
      setLoaderVersion(null);
      return;
    }
    const info = inspection?.loaders.find((item) => item.type === next);
    const preferred = info?.versions.find((item) => item.stable) ?? info?.versions[0];
    setLoaderVersion(preferred?.version ?? null);
  };

  const resetToOptions = () => {
    installJob.value = null;
    setStarting(false);
    void loadCatalog();
  };

  const start = async () => {
    if (!selectedVersion) return;
    if (loader !== 'vanilla' && !loaderVersion) {
      showToast('这个版本还没有可用的加载器。', 'warn');
      return;
    }
    setStarting(true);
    const jobId = await startInstallation({
      gameVersion: selectedVersion,
      loader,
      loaderVersion: loader === 'vanilla' ? null : loaderVersion,
      displayName: displayName.trim() || null
    });
    if (!jobId) setStarting(false);
  };

  const close = () => {
    const current = installJob.peek();
    if (current && (current.state === 'done' || current.state === 'failed' || current.state === 'cancelled')) {
      installJob.value = null;
    }
    closeInstallSheet();
  };

  const leave = (instanceId: string | undefined, launch: boolean) => {
    if (!instanceId) return;
    void updateSettings({ lastInstanceId: instanceId });
    installJob.value = null;
    closeInstallSheet();
    navigate('home');
    if (launch) void launchGame(instanceId);
  };

  const jobVersion = job?.gameVersion ?? selectedVersion;
  const jobLoader = job?.loader ?? loader;
  const jobLoaderVersion = job?.loaderVersion ?? loaderVersion;
  const chosenName =
    instances.value.find((card) => card.instance.id === job?.instanceId)?.instance.displayName ??
    (job?.displayName || displayName.trim() || `${jobVersion}${jobLoader === 'vanilla' ? '' : ` · ${loaderName(jobLoader)}`}`);

  if (job) {
    const progress = job.progress;
    const pct = progress && progress.totalBytes > 0 ? progress.completedBytes / progress.totalBytes : 0;
    const running = job.state === 'running';
    const failed = job.state === 'failed';
    const cancelled = job.state === 'cancelled';
    const done = job.state === 'done';

    return (
      <Sheet
        open={open}
        onClose={close}
        title={done ? '实例已就绪' : failed ? '安装失败' : cancelled ? '安装已取消' : '正在安装'}
        subtitle={
          done
            ? '可以启动游戏了。'
            : running
              ? '共享文件会被复用，下次安装相同版本会更快。'
              : '已下载的文件会保留，可以重新开始。'
        }
        titleId="install-sheet-title"
        footerNote={
          running ? '关闭后可在左侧查看进度。' : done ? '实例已加入列表。' : '失败时不会留下半成品实例。'
        }
        footer={
          running ? (
            <>
              <Button variant="danger" onClick={() => void cancelInstallation()}>
                取消安装
              </Button>
              <Button variant="primary" onClick={close}>
                后台继续
              </Button>
            </>
          ) : done ? (
            <>
              <Button variant="quiet" onClick={() => leave(job.instanceId, false)}>
                查看实例
              </Button>
              <Button variant="primary" icon="play" onClick={() => leave(job.instanceId, true)}>
                直接启动
              </Button>
            </>
          ) : (
            <>
              <Button variant="quiet" onClick={close}>
                关闭
              </Button>
              <Button variant="primary" icon="refresh" onClick={resetToOptions}>
                重新选择
              </Button>
            </>
          )
        }
      >
        <div class={['install-run', done ? 'install-run--done' : failed ? 'install-run--failed' : ''].filter(Boolean).join(' ')}>
          <div class="install-run__stage">
            <span class="install-run__index mono">{done ? 'READY' : failed ? 'HALT' : cancelled ? 'PAUSE' : 'BUILD'}</span>
            <div class="install-run__glyph" aria-hidden="true">
              {done ? (
                <Icon name="check" size={30} strokeWidth={1.4} />
              ) : failed ? (
                <Icon name="alert" size={30} strokeWidth={1.3} />
              ) : cancelled ? (
                <Icon name="pause" size={28} />
              ) : (
                <span class="install-run__pulse">
                  <i />
                  <i />
                  <i />
                </span>
              )}
            </div>
          </div>

          <div class="install-run__body">
            <div class="install-run__name">
              {chosenName}
              <span class="install-run__version mono">
                {jobVersion}
                {jobLoader === 'vanilla' ? '' : ` / ${loaderName(jobLoader)}${jobLoaderVersion ? ` ${jobLoaderVersion}` : ''}`}
              </span>
            </div>

            {done ? (
              <div class="install-run__message">游戏文件、实例与启动配置都已就绪。</div>
            ) : failed ? (
              <div class="install-run__message install-run__message--bad">
                {job.message || '下载或校验过程中断，可能只是网络一时不稳。'}
              </div>
            ) : cancelled ? (
              <div class="install-run__message">这次安装已经取消。已经下好的部分不会浪费。</div>
            ) : (
              <div class="install-run__message">{progress?.stageLabel ?? '正在向 Mojang 询问这个版本…'}</div>
            )}

            <div class="install-run__progress">
              <ProgressBar value={pct} indeterminate={running && (!progress || progress.totalBytes <= 0)} />
              <div class="install-run__progress-meta">
                <span class="mono">
                  {running
                    ? progress
                      ? `${Math.round(pct * 100)}%`
                      : '···'
                    : done
                      ? '100%'
                      : '—'}
                </span>
                <span class="mono">
                  {progress
                    ? `${formatBytes(progress.completedBytes)} / ${formatBytes(progress.totalBytes)}`
                    : done
                      ? formatBytes(inspection?.totalBytes)
                      : ''}
                </span>
              </div>
            </div>

            {running ? (
              <div class="install-run__terminal">
                <div class="install-run__terminal-row">
                  <span>文件</span>
                  <span class="mono">
                    {progress ? `${progress.completedFiles} / ${progress.totalFiles}` : '—'}
                  </span>
                </div>
                <div class="install-run__terminal-row">
                  <span>速度</span>
                  <span class="mono">{formatSpeed(progress?.bytesPerSecond) || '等待回应'}</span>
                </div>
                <div class="install-run__terminal-row install-run__terminal-row--item">
                  <span>正在取</span>
                  <span class="mono truncate">{progress?.currentItem ?? '版本清单'}</span>
                </div>
              </div>
            ) : done ? (
              <div class="install-run__facts">
                <div>
                  <span>游戏版本</span>
                  <b>{jobVersion}</b>
                </div>
                <div>
                  <span>加载器</span>
                  <b>{loaderName(loader)}</b>
                </div>
                <div>
                  <span>Java</span>
                  <b>{inspection ? `Java ${inspection.javaMajor}` : '自动'}</b>
                </div>
                <div>
                  <span>命名</span>
                  <b class="truncate">{chosenName}</b>
                </div>
              </div>
            ) : (
              <div class="install-run__hint">
                {failed ? '可以回到选择页重试，或者换一个下载源。' : '安装会在后台继续，可以关闭此窗口。'}
              </div>
            )}
          </div>
        </div>
      </Sheet>
    );
  }

  const busy = catalogLoading || inspecting || starting;
  const canStart = Boolean(selectedVersion) && !inspecting && !inspectError && (loader === 'vanilla' || Boolean(loaderVersion));
  const currentLoaderInfo = inspection?.loaders.find((item) => item.type === loader);

  return (
    <Sheet
      open={open}
      onClose={close}
      title="新建实例"
      subtitle="选择 Minecraft 版本与加载器，安装到独立目录。"
      titleId="install-sheet-title"
      width="wide"
      footerNote={
        inspection
          ? `资源约 ${formatBytes(inspection.totalBytes)}，共享资源只下一次。`
          : '下载完成后会校验文件大小与 SHA1。'
      }
      footer={
        <>
          <Button variant="quiet" onClick={close}>
            取消
          </Button>
          <Button
            variant="primary"
            icon="download"
            loading={starting}
            disabled={!canStart || busy && !starting}
            onClick={() => void start()}
          >
            下载并准备
          </Button>
        </>
      }
    >
      <div class="install-layout">
        <section class="install-versions">
          <div class="install-versions__head">
            <Segmented
              label="版本频道"
              value={channel}
              options={[
                { value: 'release', label: '正式版' },
                { value: 'snapshot', label: '快照与旧版' }
              ]}
              onChange={changeChannel}
            />
            <span class="install-versions__count mono">{String(versions.length).padStart(2, '0')}</span>
          </div>

          <div class="search">
            <Icon name="search" size={14} />
            <input
              class="input"
              type="search"
              value={search}
              placeholder="按版本号找，例如 1.20.1"
              aria-label="搜索游戏版本"
              spellcheck={false}
              onInput={(event) => setSearch((event.target as HTMLInputElement).value)}
            />
            {search ? (
              <button type="button" class="search__clear" aria-label="清除搜索" onClick={() => setSearch('')}>
                <Icon name="close" size={13} />
              </button>
            ) : null}
          </div>

          {catalogLoading ? (
            <div class="install-versions__loading">
              {Array.from({ length: 7 }).map(() => (
                <div class="install-version-skeleton skeleton" />
              ))}
            </div>
          ) : catalogError ? (
            <div class="install-versions__error">
              <div class="install-versions__error-title">没有取到版本清单</div>
              <div class="install-versions__error-text">{catalogError}</div>
              <Button size="sm" variant="ghost" icon="refresh" onClick={() => void loadCatalog(true)}>
                再试一次
              </Button>
            </div>
          ) : (
            <>
              <div class="install-version-list" role="radiogroup" aria-label="游戏版本">
                {shown.map((entry) => (
                  <VersionRow
                    key={entry.id}
                    entry={entry}
                    active={entry.id === selectedVersion}
                    latest={entry.id === catalog?.latest.release || entry.id === catalog?.latest.snapshot}
                    installed={installedSet.has(entry.id)}
                    onSelect={() => setSelectedVersion(entry.id)}
                  />
                ))}
                {shown.length === 0 ? (
                  <div class="install-versions__none">
                    没有找到「{search.trim()}」。换个版本号，或者切到快照里看看。
                  </div>
                ) : null}
              </div>
              {hasMore ? (
                <button type="button" class="install-versions__more" onClick={() => setVisibleCount((value) => value + 48)}>
                  还有 {versions.length - shown.length} 个版本
                  <Icon name="chevronDown" size={13} />
                </button>
              ) : null}
            </>
          )}
        </section>

        <aside class="install-picks">
          <div class="install-pick">
            <div class="install-pick__head">
              <span class="install-pick__label">加载器</span>
            </div>
            <div class="loader-list" role="radiogroup" aria-label="模组加载器">
              {LOADER_META.map((item) => {
                const info = inspection?.loaders.find((entry) => entry.type === item.id);
                const unavailable = item.id !== 'vanilla' && (!info || !info.available);
                return (
                  <button
                    key={item.id}
                    type="button"
                    role="radio"
                    aria-checked={loader === item.id}
                    class={[
                      'loader-option',
                      loader === item.id ? 'loader-option--active' : '',
                      unavailable ? 'loader-option--disabled' : ''
                    ]
                      .filter(Boolean)
                      .join(' ')}
                    disabled={unavailable || inspecting}
                    title={unavailable ? info?.note ?? '这个版本还不能装加载器' : item.note}
                    onClick={() => chooseLoader(item.id)}
                  >
                    <LoaderTile loader={item.id} size={28} />
                    <span class="loader-option__text">
                      <span class="loader-option__name">{item.label}</span>
                      <span class="loader-option__note">{unavailable ? info?.note ?? '不可用' : item.note}</span>
                    </span>
                    {loader === item.id ? <Icon name="check" size={12} /> : null}
                  </button>
                );
              })}
            </div>

            {loader !== 'vanilla' && currentLoaderInfo && currentLoaderInfo.versions.length > 0 ? (
              <div class="install-pick__sub">
                <label class="field__label" for="loader-version">
                  加载器版本
                  {!currentLoaderInfo.versions.some((entry) => entry.stable) ? (
                    <span class="field__optional">暂无稳定版</span>
                  ) : null}
                </label>
                <div class="select-wrap">
                  <select
                    id="loader-version"
                    class="select"
                    value={loaderVersion ?? ''}
                    onChange={(event) => setLoaderVersion((event.target as HTMLSelectElement).value)}
                  >
                    {currentLoaderInfo.versions.map((entry) => (
                      <option key={entry.version} value={entry.version}>
                        {entry.version}
                        {entry.stable ? '' : ' · 测试版'}
                      </option>
                    ))}
                  </select>
                  <Icon name="chevronDown" size={13} class="select-wrap__chevron" />
                </div>
              </div>
            ) : null}
          </div>

          <div class="install-pick">
            <div class="install-pick__head">
              <span class="install-pick__label">名称</span>
              <span class="mono">可选</span>
            </div>
            <Field
              label="实例名称"
              optional="留空则自动命名"
              htmlFor="install-name"
            >
              <input
                id="install-name"
                class="input"
                value={displayName}
                maxLength={24}
                placeholder={`${selectedVersion || '实例'}${loader === 'vanilla' ? '' : ` · ${loaderName(loader)}`}`}
                spellcheck={false}
                autocomplete="off"
                onInput={(event) => setDisplayName((event.target as HTMLInputElement).value)}
              />
            </Field>
          </div>

          <div class="install-summary">
            <div class="install-summary__row">
              <span>游戏</span>
              <b>{selectedVersion || '—'}</b>
            </div>
            <div class="install-summary__row">
              <span>加载器</span>
              <b>{loader === 'vanilla' ? '原版' : `${loaderName(loader)} ${loaderVersion ?? ''}`}</b>
            </div>
            <div class="install-summary__row">
              <span>目录</span>
              <b>独立 · 版本隔离</b>
            </div>
            <div class="install-summary__row">
              <span>Java</span>
              <b>{inspection ? `Java ${inspection.javaMajor}` : inspecting ? '查看中…' : '—'}</b>
            </div>
            <div class="install-summary__row">
              <span>预计占用</span>
              <b class="mono">{inspection ? formatBytes(inspection.totalBytes) : '—'}</b>
            </div>
            {inspection ? (
              <div class="install-summary__row">
                <span>发布于</span>
                <b class="mono">{formatDate(inspection.releaseTime)}</b>
              </div>
            ) : null}
            {currentInstalled ? (
              <div class="install-sheet-note">
                <Icon name="info" size={13} /> 已有相同组合。再建一个会得到独立目录，共享游戏文件。
              </div>
            ) : null}
            {inspectError ? (
              <div class="install-sheet-note install-sheet-note--bad">
                <Icon name="alert" size={13} /> {inspectError}
              </div>
            ) : null}
            {!settings.value.autoDownloadJava && inspection ? (
              <div class="install-sheet-note">
                <Icon name="info" size={13} /> 自动准备 Java 已关闭，启动前请确认本机有 Java {inspection.javaMajor}。
              </div>
            ) : null}
            {memoryInfo.value.totalMb > 0 && settings.value.autoMemory ? (
              <div class="install-summary__ram">
                内存将按机器情况自动取 {memoryInfo.value.recommendedMb} MB
              </div>
            ) : null}
          </div>
        </aside>
      </div>
    </Sheet>
  );
}

function VersionRow({
  entry,
  active,
  latest,
  installed,
  onSelect
}: {
  entry: RemoteVersion;
  active: boolean;
  latest: boolean;
  installed: boolean;
  onSelect: () => void;
}) {
  const typeLabel =
    entry.type === 'release'
      ? '正式版'
      : entry.type === 'snapshot'
        ? '快照'
        : entry.type === 'old_beta'
          ? '旧测试'
          : '旧版';
  return (
    <button
      type="button"
      role="radio"
      aria-checked={active}
      class={['install-version', active ? 'install-version--active' : ''].filter(Boolean).join(' ')}
      onClick={onSelect}
    >
      <span class="install-version__tick" aria-hidden="true" />
      <span class="install-version__main">
        <span class="install-version__id">{entry.id}</span>
        <span class="install-version__meta">
          {typeLabel} · {formatDate(entry.releaseTime)}
        </span>
      </span>
      <span class="install-version__chips">
        {latest ? <span class="chip chip--copper">最新</span> : null}
        {installed ? <span class="chip">已有</span> : null}
      </span>
    </button>
  );
}
