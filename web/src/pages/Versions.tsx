import { useEffect, useState } from 'preact/hooks';
import { formatBytes } from '../lib/format';
import {
  instances,
  launchGame,
  openConsole,
  openInstanceSettings,
  openInstallSheet,
  openModpackSheet,
  refreshSizes,
  running,
  sizesReady,
  stopGame
} from '../lib/store';
import type { InstanceCard } from '../lib/types';
import { Icon } from '../components/Icon';
import { InstanceTile } from '../components/InstanceTile';
import { Button, Empty } from '../components/ui';

const FILTERS = [
  { value: 'all', label: '全部加载器' },
  { value: 'vanilla', label: '原版' },
  { value: 'fabric', label: 'Fabric' },
  { value: 'quilt', label: 'Quilt' },
  { value: 'forge', label: 'Forge' },
  { value: 'neoforge', label: 'NeoForge' },
  { value: 'optifine', label: 'OptiFine' }
] as const;

type Filter = (typeof FILTERS)[number]['value'];

export function VersionsPage() {
  const cards = instances.value;
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const sizesKnown = sizesReady.value;

  useEffect(() => {
    void refreshSizes();
  }, []);

  const text = search.trim().toLowerCase();
  const filtered = cards.filter((card) => {
    if (filter !== 'all' && card.instance.loader !== filter) return false;
    if (!text) return true;
    return `${card.instance.displayName} ${card.instance.gameVersion} ${card.instance.loader}`
      .toLowerCase()
      .includes(text);
  });
  const totalBytes = cards.reduce((sum, card) => sum + (card.sizeBytes || 0), 0);
  const runningId = running.value.running ? running.value.instanceId : null;

  return (
    <div class="page versions">
      <header class="home-head">
        <div>
          <h1 class="page-title">实例</h1>
          <p class="page-sub">
            {cards.length === 0
              ? '安装游戏与加载器，创建一个独立实例。'
              : `${cards.length} 个实例 · ${sizesKnown ? formatBytes(totalBytes) : '正在计算占用'} · 存档、模组与配置互相隔离`}
          </p>
        </div>
        <div class="page-head__actions">
          <Button variant="ghost" icon="box" onClick={openModpackSheet}>
            导入整合包
          </Button>
          <Button variant="primary" icon="plus" onClick={openInstallSheet}>
            新建实例
          </Button>
        </div>
      </header>

      <div class="instance-toolbar">
        <div class="search instance-toolbar__search">
          <Icon name="search" size={15} />
          <input
            class="input"
            type="search"
            value={search}
            placeholder="搜索实例、版本或加载器"
            aria-label="搜索实例"
            spellcheck={false}
            onInput={(event) => setSearch((event.target as HTMLInputElement).value)}
          />
          {search ? (
            <button type="button" class="search__clear" aria-label="清除搜索" onClick={() => setSearch('')}>
              <Icon name="close" size={13} />
            </button>
          ) : null}
        </div>
        <div class="select-wrap instance-toolbar__filter">
          <select
            class="select"
            value={filter}
            aria-label="按加载器筛选"
            onChange={(event) => setFilter((event.target as HTMLSelectElement).value as Filter)}
          >
            {FILTERS.map((item) => (
              <option key={item.value} value={item.value}>{item.label}</option>
            ))}
          </select>
          <Icon name="chevronDown" size={13} class="select-wrap__chevron" />
        </div>
        <span class="instance-toolbar__count">{filtered.length} 个</span>
      </div>

      {cards.length === 0 ? (
        <Empty icon="grid" title="还没有实例" text="原版、Fabric、Quilt、Forge、NeoForge，选一个开始。" boxed>
          <Button variant="primary" icon="plus" onClick={openInstallSheet}>
            新建实例
          </Button>
        </Empty>
      ) : filtered.length === 0 ? (
        <div class="versions-empty">
          <div class="versions-empty__title">没有匹配的实例</div>
          <button type="button" class="text-link" onClick={() => { setSearch(''); setFilter('all'); }}>
            清除筛选
          </button>
        </div>
      ) : (
        <div class="instance-grid-tiles instance-grid-tiles--page">
          {filtered.map((card) => (
            <InstanceTile
              key={card.instance.id}
              card={card}
              onPlay={(id) => {
                if (runningId) openConsole('logs');
                else void launchGame(id);
              }}
              onStop={() => void stopGame()}
            />
          ))}
          <button type="button" class="instance-tile instance-tile--add" onClick={openInstallSheet}>
            <span class="instance-tile__icon"><Icon name="plus" size={24} /></span>
            <span class="instance-tile__name">新建实例</span>
            <span class="instance-tile__meta">选择版本与加载器</span>
          </button>
        </div>
      )}

      <footer class="page-foot">
        <div class="page-foot__group">
          <span>{cards.length} 个实例</span>
          <span>{sizesKnown ? `${formatBytes(totalBytes)} 占用` : '正在计算占用…'}</span>
        </div>
        <div class="page-foot__group">
          <span>原版 · Fabric · Quilt · Forge · NeoForge</span>
        </div>
      </footer>
    </div>
  );
}
