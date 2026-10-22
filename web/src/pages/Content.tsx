import { useEffect, useRef, useState } from 'preact/hooks';
import type { CSSProperties } from 'preact';
import { request } from '../lib/bridge';
import {
  formatBytes,
  formatCount,
  formatShortDate,
  loaderName,
  projectTypeName
} from '../lib/format';
import {
  cancelContentJob,
  contentJobs,
  contentRevision,
  errorMessage,
  instances,
  launchGame,
  openConsole,
  openInstallSheet,
  registerContentJob,
  running,
  shaderProgress,
  settings,
  showToast
} from '../lib/store';
import type { InstalledContent, InstanceCard, ModrinthHit, ModrinthSearchResult, ShaderRecommendation } from '../lib/types';
import { ContentDetailSheet } from '../components/ContentDetailSheet';
import { Icon } from '../components/Icon';
import { LoaderTile } from '../components/LoaderTile';
import { Button, ConfirmDialog, Empty, ProgressBar, Segmented, Toggle } from '../components/ui';

type ContentType = 'mod' | 'resourcepack' | 'shader';
type View = 'installed' | 'discover';
type Source = 'modrinth' | 'curseforge';

const TYPE_OPTIONS: Array<{ value: ContentType; label: string }> = [
  { value: 'mod', label: '模组' },
  { value: 'resourcepack', label: '资源包' },
  { value: 'shader', label: '光影' }
];

const TYPE_NOUN: Record<ContentType, string> = {
  mod: '模组',
  resourcepack: '资源包',
  shader: '光影'
};

export function ContentPage() {
  const cards = instances.value;
  const [selectedId, setSelectedId] = useState<string>(() => initialInstanceId(cards));
  const [type, setType] = useState<ContentType>('mod');
  const [view, setView] = useState<View>('installed');
  const [source, setSource] = useState<Source>('modrinth');
  const [curseLink, setCurseLink] = useState('');
  const [curseBusy, setCurseBusy] = useState(false);
  const [shaderRec, setShaderRec] = useState<ShaderRecommendation | null>(null);
  const [shaderBusy, setShaderBusy] = useState(false);
  const [installed, setInstalled] = useState<InstalledContent[]>([]);
  const [installedLoading, setInstalledLoading] = useState(false);
  const [installedQuery, setInstalledQuery] = useState('');
  const [hits, setHits] = useState<ModrinthHit[]>([]);
  const [totalHits, setTotalHits] = useState(0);
  const [discoverLoading, setDiscoverLoading] = useState(false);
  const [discoverError, setDiscoverError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<'relevance' | 'downloads' | 'newest'>('relevance');
  const [retryToken, setRetryToken] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [detail, setDetail] = useState<ModrinthHit | null>(null);
  const [removeTarget, setRemoveTarget] = useState<InstalledContent | null>(null);
  const [removing, setRemoving] = useState(false);
  const [busyProject, setBusyProject] = useState<string | null>(null);
  const revision = contentRevision.value;
  const searchSeq = useRef(0);

  useEffect(() => {
    if (cards.length === 0) {
      if (selectedId) setSelectedId('');
      return;
    }
    if (!cards.some((card) => card.instance.id === selectedId)) {
      setSelectedId(initialInstanceId(cards));
    }
  }, [cards, selectedId]);

  const card = cards.find((entry) => entry.instance.id === selectedId) ?? null;
  const loader = card?.instance.loader ?? 'vanilla';
  const vanillaBlocked = (loader === 'vanilla' || loader === 'optifine') && type === 'mod';

  useEffect(() => {
    if (!selectedId) {
      setInstalled([]);
      return;
    }
    let alive = true;
    setInstalledLoading(true);
    request<InstalledContent[]>('modrinth.list', { id: selectedId }, 30000)
      .then((items) => {
        if (alive) setInstalled(items ?? []);
      })
      .catch((error) => {
        if (alive) showToast('无法读取已装内容', 'error', errorMessage(error));
      })
      .finally(() => {
        if (alive) setInstalledLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [selectedId, revision]);

  useEffect(() => {
    if (view !== 'discover' || !selectedId) return;
    if (vanillaBlocked) {
      setHits([]);
      setTotalHits(0);
      setDiscoverError(null);
      setDiscoverLoading(false);
      return;
    }
    const seq = ++searchSeq.current;
    setDiscoverLoading(true);
    setDiscoverError(null);
    const timer = window.setTimeout(() => {
      request<ModrinthSearchResult>(
        'modrinth.search',
        {
          instanceId: selectedId,
          projectType: type,
          query: query.trim(),
          sort,
          source,
          offset: 0,
          limit: 20
        },
        45000
      )
        .then((result) => {
          if (seq !== searchSeq.current) return;
          setHits(result?.hits ?? []);
          setTotalHits(result?.totalHits ?? result?.hits?.length ?? 0);
        })
        .catch((error) => {
          if (seq !== searchSeq.current) return;
          setHits([]);
          setTotalHits(0);
          setDiscoverError(errorMessage(error));
        })
        .finally(() => {
          if (seq === searchSeq.current) setDiscoverLoading(false);
        });
    }, 300);
    return () => window.clearTimeout(timer);
  }, [view, selectedId, type, query, sort, source, vanillaBlocked, retryToken]);

  useEffect(() => {
    if (!selectedId || type !== 'shader') {
      setShaderRec(null);
      return;
    }
    let alive = true;
    request<ShaderRecommendation>('shader.recommend', { id: selectedId }, 20000)
      .then((result) => { if (alive) setShaderRec(result); })
      .catch(() => { if (alive) setShaderRec(null); });
    return () => { alive = false; };
  }, [selectedId, type, revision]);

  const installedOfType = installed.filter((item) => item.projectType === type);
  const visibleInstalled = installedOfType.filter((item) => {
    const text = installedQuery.trim().toLowerCase();
    return !text || `${item.title} ${item.fileName} ${item.versionNumber ?? ''}`.toLowerCase().includes(text);
  });
  const runningJobs = Object.values(contentJobs.value).filter(
    (job) => job.state === 'running' && job.instanceId === selectedId
  );
  const detailHit = detail
    ? { ...detail, installed: detail.installed || installed.some((item) => item.projectId === detail.projectId) }
    : null;

  const changeType = (next: ContentType) => {
    setType(next);
    setHits([]);
    setDiscoverError(null);
  };

  const toggle = async (item: InstalledContent, enabled: boolean) => {
    setBusyProject(item.projectId);
    setInstalled((list) =>
      list.map((entry) => (entry.projectId === item.projectId ? { ...entry, enabled } : entry))
    );
    try {
      await request('modrinth.toggle', {
        instanceId: selectedId,
        projectId: item.projectId,
        enabled
      });
    } catch (error) {
      setInstalled((list) =>
        list.map((entry) => (entry.projectId === item.projectId ? { ...entry, enabled: !enabled } : entry))
      );
      showToast('没有切换成功', 'error', errorMessage(error));
    } finally {
      setBusyProject(null);
    }
  };

  const update = async (item: InstalledContent) => {
    setBusyProject(item.projectId);
    try {
      const result = await request<{ accepted: boolean; requestId: string }>(
        'modrinth.update',
        {
          instanceId: selectedId,
          projectId: item.projectId,
          projectType: item.projectType
        },
        30000
      );
      if (result?.requestId) {
        void registerContentJob(result.requestId, {
          projectId: item.projectId,
          title: item.title,
          instanceId: selectedId
        });
        showToast(`正在取 ${item.title} 的新版本`, 'info');
      }
    } catch (error) {
      showToast('无法更新', 'error', errorMessage(error));
    } finally {
      setBusyProject(null);
    }
  };

  const remove = async () => {
    if (!removeTarget) return;
    setRemoving(true);
    try {
      await request('modrinth.remove', {
        instanceId: selectedId,
        projectId: removeTarget.projectId,
        projectType: removeTarget.projectType
      });
      setInstalled((list) => list.filter((entry) => entry.projectId !== removeTarget.projectId));
      showToast(`${removeTarget.title} 已被移走。`, 'info');
      setRemoveTarget(null);
    } catch (error) {
      showToast('无法删除', 'error', errorMessage(error));
    } finally {
      setRemoving(false);
    }
  };

  const loadMore = async () => {
    if (loadingMore || discoverLoading) return;
    setLoadingMore(true);
    try {
      const result = await request<ModrinthSearchResult>(
        'modrinth.search',
        {
          instanceId: selectedId,
          projectType: type,
          query: query.trim(),
          sort,
          source,
          offset: hits.length,
          limit: 20
        },
        45000
      );
      const additions = result?.hits ?? [];
      setHits((current) => {
        const known = new Set(current.map((hit) => hit.projectId));
        return current.concat(additions.filter((hit) => !known.has(hit.projectId)));
      });
      setTotalHits(result?.totalHits ?? hits.length + additions.length);
    } catch (error) {
      showToast('没有取到更多内容', 'error', errorMessage(error));
    } finally {
      setLoadingMore(false);
    }
  };

  const installCurseLink = async () => {
    const link = curseLink.trim();
    if (!link || curseBusy) return;
    setCurseBusy(true);
    try {
      const result = await request<{
        file: { projectId: number; fileId: number; title: string; name: string; projectType: string };
        job: { requestId: string };
      }>('curseforge.installLink', {
        instanceId: selectedId,
        link,
        projectType: type
      }, 60000);
      if (result?.job?.requestId) {
        void registerContentJob(result.job.requestId, {
          projectId: `cf-${result.file.projectId}`,
          title: result.file.title,
          instanceId: selectedId
        });
        setCurseLink('');
        showToast(`正在安装 ${result.file.title}`, 'success');
      }
    } catch (error) {
      showToast('无法安装 CurseForge 项目', 'error', errorMessage(error));
    } finally {
      setCurseBusy(false);
    }
  };

  const installShader = async () => {
    if (!shaderRec || shaderBusy) return;
    setShaderBusy(true);
    try {
      const result = await request<{ kind: string; job?: { requestId: string } }>('shader.install', {
        instanceId: selectedId,
        kind: shaderRec.kind
      }, 30000);
      if (result?.job?.requestId) {
        void registerContentJob(result.job.requestId, {
          projectId: 'YL57xq9U',
          title: 'Iris',
          instanceId: selectedId
        });
      }
      showToast(`正在安装 ${shaderRec.label}`, 'success');
    } catch (error) {
      showToast('无法安装光影组件', 'error', errorMessage(error));
    } finally {
      setShaderBusy(false);
    }
  };

  return (
    <div class="page content-page">
      <header class="page-head">
        <div class="page-head__main">
          <h1 class="page-title">内容</h1>
          <p class="page-sub">
            {card
              ? `为「${card.instance.displayName}」安装模组、资源包与光影。`
              : '先创建一个实例，再安装内容。'}
          </p>
        </div>
        <div class="page-head__actions">
          <Button variant="ghost" icon="plus" onClick={openInstallSheet}>
            新建实例
          </Button>
        </div>
      </header>

      {cards.length === 0 ? (
        <Empty icon="package" title="还没有可安装内容的实例" text="内容需要放在实例目录里。先新建一个实例。" boxed>
          <Button variant="primary" icon="plus" onClick={openInstallSheet}>
            新建第一个实例
          </Button>
        </Empty>
      ) : (
        <>
          <div class="content-instances" aria-label="选择实例">
            {cards.map((entry) => {
              const active = entry.instance.id === selectedId;
              const isRunning = running.value.running && running.value.instanceId === entry.instance.id;
              return (
                <button
                  type="button"
                  key={entry.instance.id}
                  class={['content-instance', active ? 'content-instance--active' : ''].filter(Boolean).join(' ')}
                  onClick={() => setSelectedId(entry.instance.id)}
                  aria-pressed={active}
                >
                  <span class="content-instance__mark">
                    <LoaderTile loader={entry.instance.loader} size={24} />
                  </span>
                  <span class="content-instance__text">
                    <span class="content-instance__name">{entry.instance.displayName}</span>
                    <span class="content-instance__meta">
                      {entry.instance.gameVersion} · {loaderName(entry.instance.loader)}
                    </span>
                  </span>
                  {isRunning ? <span class="live-dot" /> : null}
                </button>
              );
            })}
          </div>

          <div class="content-source-row">
            <Segmented
              label="内容源"
              value={source}
              options={[
                { value: 'modrinth', label: 'Modrinth' },
                { value: 'curseforge', label: 'CurseForge' }
              ]}
              onChange={(next) => {
                setSource(next as Source);
                setHits([]);
                setDiscoverError(null);
              }}
            />
            <span class="content-source-note">
              {source === 'curseforge'
                ? '搜索需要在设置中填写 API Key；也可以直接粘贴项目链接。'
                : '来自 Modrinth 的模组、资源包与光影。'}
            </span>
          </div>

          <div class="content-toolbar">
            <Segmented
              label="内容视图"
              value={view}
              options={[
                { value: 'installed', label: `已装 ${installed.length}` },
                { value: 'discover', label: '去发现' }
              ]}
              onChange={(next) => setView(next as View)}
            />
            <div class="content-toolbar__types">
              <Segmented
                label="内容类型"
                value={type}
                options={TYPE_OPTIONS.map((option) => ({
                  value: option.value,
                  label: option.label,
                  title: option.value === 'mod' && (loader === 'vanilla' || loader === 'optifine') ? '这个实例不能装模组' : undefined
                }))}
                onChange={changeType}
              />
            </div>
            <div class="content-toolbar__spacer" />
            {view === 'installed' ? (
              <div class="search content-toolbar__search">
                <Icon name="search" size={14} />
                <input
                  class="input"
                  type="search"
                  value={installedQuery}
                  placeholder={`在这 ${installedOfType.length} 份${TYPE_NOUN[type]}里找`}
                  aria-label="筛选已装内容"
                  onInput={(event) => setInstalledQuery((event.target as HTMLInputElement).value)}
                />
              </div>
            ) : (
              <>
                <div class="search content-toolbar__search">
                  <Icon name="search" size={14} />
                  <input
                    class="input"
                    type="search"
                    value={query}
                    placeholder={`搜索 ${TYPE_NOUN[type]}`}
                    aria-label={`搜索 ${TYPE_NOUN[type]}`}
                    onInput={(event) => setQuery((event.target as HTMLInputElement).value)}
                  />
                </div>
                <div class="select-wrap content-toolbar__sort">
                  <select
                    class="select"
                    value={sort}
                    aria-label="排序方式"
                    onChange={(event) => setSort((event.target as HTMLSelectElement).value as typeof sort)}
                  >
                    <option value="relevance">相关</option>
                    <option value="downloads">最多下载</option>
                    <option value="newest">最新</option>
                  </select>
                  <Icon name="chevronDown" size={12} class="select-wrap__chevron" />
                </div>
              </>
            )}
          </div>

          {source === 'curseforge' && view === 'discover' ? (
            <div class="curse-link-panel">
              <div class="curse-link-panel__copy">
                <b>从 CurseForge 链接安装</b>
                <span>粘贴项目页地址，例如 curseforge.com/minecraft/mc-mods/jei</span>
              </div>
              <div class="inline-form">
                <input
                  class="input"
                  value={curseLink}
                  spellcheck={false}
                  placeholder="https://www.curseforge.com/minecraft/..."
                  aria-label="CurseForge 项目链接"
                  onInput={(event) => setCurseLink((event.target as HTMLInputElement).value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void installCurseLink();
                  }}
                />
                <Button variant="primary" loading={curseBusy} onClick={() => void installCurseLink()}>
                  安装
                </Button>
              </div>
            </div>
          ) : null}

          {type === 'shader' && shaderRec ? (
            <div class={['shader-support', shaderRec.installed ? 'shader-support--ready' : ''].filter(Boolean).join(' ')}>
              <div class="shader-support__body">
                <b>{shaderRec.installed ? `${shaderRec.label} 已安装` : `安装 ${shaderRec.label}`}</b>
                <span>{shaderRec.installed ? '把光影包放进 shaderpacks 文件夹即可。' : shaderRec.hint}</span>
              </div>
              {shaderRec.installed ? (
                <span class="chip chip--solid">可用</span>
              ) : (
                <Button
                  size="sm"
                  variant="primary"
                  icon="download"
                  disabled={!shaderRec.supported || shaderBusy || Boolean(shaderProgress.value)}
                  onClick={() => void installShader()}
                >
                  {shaderProgress.value?.message ?? `安装 ${shaderRec.label}`}
                </Button>
              )}
            </div>
          ) : null}

          {runningJobs.length > 0 ? (
            <div class="content-jobs">
              {runningJobs.map((job) => {
                const progress = job.progress;
                const pct = progress && progress.totalBytes > 0 ? progress.receivedBytes / progress.totalBytes : 0;
                return (
                  <div class="content-job" key={job.requestId}>
                    <span class="content-job__pulse" aria-hidden="true" />
                    <div class="content-job__body">
                      <div class="content-job__line">
                        <span>{job.title || '正在安装'}</span>
                        <span class="mono">
                          {progress ? `${Math.round(pct * 100)}%` : '···'}
                        </span>
                      </div>
                      <ProgressBar value={pct} thin indeterminate={!progress || progress.totalBytes <= 0} />
                    </div>
                    <button type="button" class="text-link" onClick={() => void cancelContentJob(job.requestId)}>
                      取消
                    </button>
                  </div>
                );
              })}
            </div>
          ) : null}

          {vanillaBlocked ? (
            <div class="content-blocked">
              <div class="content-blocked__mark">
                <Icon name="layers" size={20} />
              </div>
              <div class="content-blocked__body">
                <div class="content-blocked__title">{loader === 'optifine' ? 'OptiFine 实例不能安装模组' : '原版实例不能安装模组'}</div>
                <div class="content-blocked__text">
                  这个实例没有模组加载器。可以安装资源包或光影，或者新建一个 Fabric / Forge / NeoForge 实例。
                </div>
              </div>
              <Button variant="ghost" icon="plus" onClick={openInstallSheet}>
                新建实例
              </Button>
            </div>
          ) : view === 'installed' ? (
            <InstalledList
              items={visibleInstalled}
              loading={installedLoading}
              instance={card}
              type={type}
              busyProject={busyProject}
              jobs={runningJobs}
              onToggle={(item, enabled) => void toggle(item, enabled)}
              onUpdate={(item) => void update(item)}
              onRemove={setRemoveTarget}
              onDiscover={() => setView('discover')}
            />
          ) : discoverError ? (
            <div class="content-discover-error">
              <div class="content-discover-error__title">
                {source === 'curseforge' ? 'CurseForge 没有响应' : '没有连上 Modrinth'}
              </div>
              <div class="content-discover-error__text">{discoverError}</div>
              <Button size="sm" variant="ghost" icon="refresh" onClick={() => setRetryToken((value) => value + 1)}>
                再试一次
              </Button>
            </div>
          ) : discoverLoading && hits.length === 0 ? (
            <div class="project-grid">
              {Array.from({ length: 6 }).map(() => (
                <div class="project-skeleton">
                  <div class="project-skeleton__icon skeleton" />
                  <div class="project-skeleton__lines">
                    <div class="skeleton skeleton--line" style={{ width: '45%' }} />
                    <div class="skeleton skeleton--line" style={{ width: '80%' }} />
                    <div class="skeleton skeleton--line" style={{ width: '65%' }} />
                  </div>
                </div>
              ))}
            </div>
          ) : hits.length === 0 ? (
            <Empty
              icon="search"
              title={query ? `没有找到「${query.trim()}」` : `还没有找到合适${TYPE_NOUN[type]}`}
              text="换一个词，或者换一个内容类型看看。Modrinth 上的东西很多，值得多翻几页。"
            />
          ) : (
            <>
              <div class="project-grid">
                {hits.map((hit) => (
                  <button
                    type="button"
                    class="project-card"
                    key={hit.projectId}
                    onClick={() => setDetail(hit)}
                  >
                    <span class="project-card__top">
                      <span class="content-icon" style={iconStyle(hit)}>
                        {hit.iconUrl ? (
                          <img src={hit.iconUrl} alt="" loading="lazy" referrerpolicy="no-referrer" />
                        ) : (
                          <span>{hit.title.slice(0, 1).toUpperCase()}</span>
                        )}
                      </span>
                      <span class="project-card__ident">
                        <span class="project-card__title">{hit.title}</span>
                        <span class="project-card__author">{hit.author}</span>
                      </span>
                      {hit.installed ? <span class="chip chip--moss">已装</span> : null}
                    </span>
                    <span class="project-card__desc">{hit.description}</span>
                    <span class="project-card__foot">
                      <span class="project-card__cats">
                        {hit.categories.slice(0, 2).map((category) => (
                          <span class="chip chip--bare" key={category}>
                            {category}
                          </span>
                        ))}
                      </span>
                      <span class="project-card__numbers mono">
                        <Icon name="download" size={11} /> {formatCount(hit.downloads)}
                      </span>
                    </span>
                  </button>
                ))}
              </div>
              {hits.length < totalHits ? (
                <div class="content-more">
                  <Button variant="ghost" loading={loadingMore} onClick={() => void loadMore()}>
                    再看一些 · 还有 {totalHits - hits.length}
                  </Button>
                </div>
              ) : (
                <div class="content-more content-more--end">
                  <span class="mono">END OF THIS SHELF · {totalHits}</span>
                </div>
              )}
            </>
          )}

          {card?.running ? (
            <footer class="content-running-note">
              <span class="live-dot" />
              这个实例正在运行。现在装的模组需要重启游戏后才会加载。
              <button
                type="button"
                class="text-link"
                onClick={() => (running.value.running ? openConsole('logs') : void launchGame(card.instance.id))}
              >
                查看日志
              </button>
            </footer>
          ) : null}
        </>
      )}

      <footer class="page-foot">
        <div class="page-foot__group">
          <span>{card ? `${card.instance.displayName} · ${installed.length} 份内容` : 'MODRINTH'}</span>
        </div>
        <div class="page-foot__group">
          <span>内容版权归各自作者所有</span>
        </div>
      </footer>

      <ContentDetailSheet
        open={Boolean(detail)}
        hit={detailHit}
        instance={card}
        source={source}
        onClose={() => setDetail(null)}
      />

      <ConfirmDialog
        open={Boolean(removeTarget)}
        title={`移走「${removeTarget?.title ?? ''}」？`}
        message={
          <>
            <b>{removeTarget?.fileName}</b> 会从这个实例里删除。已经生成的存档不会被影响。
          </>
        }
        confirmLabel="移走"
        tone="danger"
        busy={removing}
        onConfirm={() => void remove()}
        onCancel={() => setRemoveTarget(null)}
      />
    </div>
  );
}

function InstalledList({
  items,
  loading,
  instance,
  type,
  busyProject,
  jobs,
  onToggle,
  onUpdate,
  onRemove,
  onDiscover
}: {
  items: InstalledContent[];
  loading: boolean;
  instance: InstanceCard | null;
  type: ContentType;
  busyProject: string | null;
  jobs: Array<{ projectId: string; requestId: string }>;
  onToggle: (item: InstalledContent, enabled: boolean) => void;
  onUpdate: (item: InstalledContent) => void;
  onRemove: (item: InstalledContent) => void;
  onDiscover: () => void;
}) {
  if (loading && items.length === 0) {
    return (
      <div class="content-list content-list--loading">
        {Array.from({ length: 4 }).map(() => (
          <div class="content-row content-row--skeleton">
            <div class="content-icon skeleton" />
            <div class="content-row__lines">
              <div class="skeleton skeleton--line" style={{ width: '30%' }} />
              <div class="skeleton skeleton--line" style={{ width: '60%' }} />
            </div>
          </div>
        ))}
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <Empty
        icon="package"
        title={type === 'mod' ? '这页还没有模组' : type === 'resourcepack' ? '还没有资源包' : '还没有光影'}
        text={
          type === 'mod'
            ? 'Sodium、Lithium 这类模组可以降低渲染与逻辑开销。'
            : type === 'shader'
              ? '光影需要 Iris 或 OptiFine；Fabric 实例可以直接安装 Iris。'
              : '材质包会改变方块与天空的模样，不改动玩法。'
        }
        boxed
      >
        <Button variant="primary" icon="search" onClick={onDiscover}>
          去发现里找
        </Button>
        {instance ? (
          <Button variant="quiet" icon="folder" onClick={() => void request('instance.openFolder', { id: instance.instance.id })}>
            打开实例目录
          </Button>
        ) : null}
      </Empty>
    );
  }

  return (
    <div class="content-list">
      <div class="content-list__head">
        <span>已装的内容</span>
        <span class="mono">{formatBytes(items.reduce((sum, item) => sum + item.sizeBytes, 0))}</span>
      </div>
      {items.map((item) => {
        const activeJob = jobs.find((job) => job.projectId === item.projectId);
        const busy = busyProject === item.projectId || Boolean(activeJob);
        return (
          <div class={['content-row', !item.enabled ? 'content-row--off' : ''].filter(Boolean).join(' ')} key={item.projectId}>
            <span class="content-icon content-icon--row">
              {item.iconUrl ? (
                <img src={item.iconUrl} alt="" loading="lazy" referrerpolicy="no-referrer" />
              ) : (
                <span>{item.title.slice(0, 1).toUpperCase()}</span>
              )}
            </span>
            <div class="content-row__main">
              <div class="content-row__title">
                {item.title}
                {!item.fileExists ? <span class="chip chip--clay">文件不见了</span> : null}
                {!item.enabled ? <span class="chip">已停用</span> : null}
                {activeJob ? (
                  <span class="content-row__busy">
                    <span class="btn-spinner" /> 更新中
                  </span>
                ) : null}
              </div>
              <div class="content-row__meta">
                <span class="mono">{item.versionNumber ?? '—'}</span>
                <span class="dot-sep">·</span>
                <span class="truncate" title={item.fileName}>{item.fileName}</span>
                <span class="dot-sep">·</span>
                <span class="mono">{formatBytes(item.sizeBytes)}</span>
                <span class="dot-sep">·</span>
                <span>{formatShortDate(item.installedAt)}</span>
              </div>
            </div>
            <div class="content-row__actions">
              <Toggle
                checked={item.enabled}
                label={`${item.enabled ? '停用' : '启用'} ${item.title}`}
                disabled={busy}
                onChange={(enabled) => onToggle(item, enabled)}
              />
              <button
                type="button"
                class="icon-btn"
                aria-label={`更新 ${item.title}`}
                title="重新取一个最新兼容版本"
                disabled={busy}
                onClick={() => onUpdate(item)}
              >
                <Icon name="refresh" size={14} />
              </button>
              <button
                type="button"
                class="icon-btn icon-btn--danger"
                aria-label={`删除 ${item.title}`}
                title="移走"
                disabled={busy}
                onClick={() => onRemove(item)}
              >
                <Icon name="trash" size={14} />
              </button>
            </div>
          </div>
        );
      })}
    </div>
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

function initialInstanceId(cards: InstanceCard[]): string {
  if (cards.length === 0) return '';
  const preferred = settings.value.lastInstanceId;
  return (
    cards.find((card) => card.instance.id === preferred)?.instance.id ?? cards[0].instance.id
  );
}
