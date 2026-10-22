import { request } from '../lib/bridge';
import { NAV_ITEMS, navigate, route } from '../lib/router';
import {
  accountSheetOpen,
  accounts,
  activeAccount,
  appInfo,
  installJob,
  instances,
  openConsole,
  openInstallSheet,
  running
} from '../lib/store';
import { BrandMark, Icon } from './Icon';
import { SkinFace } from './Skin';

export function Sidebar() {
  const account = activeAccount.value;
  const job = installJob.value;
  const installing = job?.state === 'running';
  const pct = job?.progress && job.progress.totalBytes > 0
    ? Math.min(100, Math.round((job.progress.completedBytes / job.progress.totalBytes) * 100))
    : 0;
  const runningCard = running.value.instanceId
    ? instances.value.find((card) => card.instance.id === running.value.instanceId)
    : null;
  const version = appInfo.value?.version ?? '0.1.1';

  return (
    <aside class="rail">
      <button type="button" class="rail__brand" aria-label="回到首页" onClick={() => navigate('home')}>
        <BrandMark size={26} />
      </button>

      <nav class="rail__nav" aria-label="主导航">
        {NAV_ITEMS.map((item) => {
          const active = route.value === item.id;
          return (
            <button
              key={item.id}
              type="button"
              class={['rail__btn', active ? 'rail__btn--active' : ''].filter(Boolean).join(' ')}
              aria-current={active ? 'page' : undefined}
              aria-label={item.label}
              title={item.label}
              onClick={() => navigate(item.id)}
            >
              <Icon name={navIcon(item.id)} size={20} />
              <span class="rail__tip">{item.label}</span>
            </button>
          );
        })}
      </nav>

      <button type="button" class="rail__btn rail__btn--add" aria-label="新建实例" title="新建实例" onClick={openInstallSheet}>
        <Icon name="plus" size={20} />
        <span class="rail__tip">新建实例</span>
      </button>

      <div class="rail__spacer" />

      {running.value.running ? (
        <button type="button" class="rail__btn rail__btn--live" aria-label="查看运行日志" title="查看运行日志" onClick={() => openConsole('logs')}>
          <span class="live-dot live-dot--pulse" />
          <span class="rail__tip">{runningCard?.instance.displayName ?? '游戏运行中'}</span>
        </button>
      ) : null}

      {installing ? (
        <button type="button" class="rail__btn rail__btn--progress" aria-label="查看安装进度" title={`安装中 ${pct}%`} onClick={openInstallSheet}>
          <span class="rail__progress-ring" style={{ '--pct': `${pct * 3.6}deg` }} />
          <span class="rail__tip">安装中 {pct}%</span>
        </button>
      ) : null}

      <button
        type="button"
        class="rail__account"
        aria-label={account ? `当前账号 ${account.name}` : '登录账号'}
        title={account ? account.name : '登录账号'}
        onClick={() => {
          accountSheetOpen.value = true;
        }}
      >
        {account ? <SkinFace account={account} class="rail__face" /> : <Icon name="user" size={18} />}
        {accounts.value.items.length > 1 ? <span class="rail__account-count">{accounts.value.items.length}</span> : null}
      </button>

      <button type="button" class="rail__btn rail__btn--small" aria-label="打开数据目录" title="打开数据目录" onClick={() => void request('shell.openData')}>
        <Icon name="folder" size={17} />
        <span class="rail__tip">数据目录</span>
      </button>

      <span class="rail__version">v{version}</span>
    </aside>
  );
}

function navIcon(id: string) {
  if (id === 'versions') return 'grid' as const;
  if (id === 'content') return 'box' as const;
  if (id === 'settings') return 'settings' as const;
  return 'home' as const;
}
