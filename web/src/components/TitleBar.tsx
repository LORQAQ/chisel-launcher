import { useRef } from 'preact/hooks';
import { request } from '../lib/bridge';
import { NAV_ITEMS, route } from '../lib/router';
import { openConsole, ready, running, settings, windowMaximized } from '../lib/store';
import { BrandMark, Icon } from './Icon';

export function TitleBar() {
  const start = useRef<{ x: number; y: number } | null>(null);
  const moved = useRef(false);
  const current =
    !ready.value || !settings.value.onboarded
      ? { label: '初次启动', latin: '' }
      : NAV_ITEMS.find((item) => item.id === route.value) ?? NAV_ITEMS[0];

  const onPointerDown = (event: PointerEvent) => {
    if (event.button !== 0) return;
    const target = event.target as HTMLElement;
    if (target.closest('button, a, input, select')) return;
    start.current = { x: event.screenX, y: event.screenY };
    moved.current = false;
    const onMove = (moveEvent: PointerEvent) => {
      if (!start.current || moved.current) return;
      const distance = Math.hypot(moveEvent.screenX - start.current.x, moveEvent.screenY - start.current.y);
      if (distance < 3) return;
      moved.current = true;
      cleanup();
      void request('window.drag');
    };
    const cleanup = () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', cleanup);
      window.removeEventListener('pointercancel', cleanup);
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', cleanup);
    window.addEventListener('pointercancel', cleanup);
  };

  const onDoubleClick = (event: MouseEvent) => {
    const target = event.target as HTMLElement;
    if (target.closest('button, a, input, select')) return;
    void toggleMaximize();
  };

  return (
    <header class="titlebar">
      <div class="titlebar__brand">
        <BrandMark size={22} class="titlebar__mark" />
      </div>

      <div class="titlebar__drag" onPointerDown={onPointerDown} onDblClick={onDoubleClick}>
        <span class="titlebar__app">凿境</span>
        <div class="titlebar__trail">
          <b>{current.label}</b>
        </div>
        {running.value.running ? (
          <button class="titlebar__live" type="button" onClick={() => openConsole('logs')}>
            <span class="live-dot live-dot--pulse" />
            游戏运行中
          </button>
        ) : null}
      </div>

      <div class="window-controls">
        <button type="button" aria-label="最小化" title="最小化" onClick={() => void request('window.minimize')}>
          <Icon name="minus" size={14} />
        </button>
        <button
          type="button"
          aria-label={windowMaximized.value ? '还原窗口' : '最大化'}
          title={windowMaximized.value ? '还原窗口' : '最大化'}
          onClick={() => void toggleMaximize()}
        >
          <Icon name={windowMaximized.value ? 'restore' : 'maximize'} size={13} />
        </button>
        <button
          type="button"
          class="window-controls__close"
          aria-label="关闭"
          title="关闭"
          onClick={() => void request('window.close')}
        >
          <Icon name="close" size={14} />
        </button>
      </div>
    </header>
  );
}

async function toggleMaximize() {
  try {
    const result = await request<{ maximized: boolean }>('window.toggleMaximize');
    if (typeof result?.maximized === 'boolean') windowMaximized.value = result.maximized;
    else windowMaximized.value = !windowMaximized.value;
  } catch {
    /* the window host owns the state */
  }
}
