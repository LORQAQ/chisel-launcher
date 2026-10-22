import { Icon } from './Icon';
import { InstanceAvatar } from './InstanceAvatar';
import { openConsole, openInstanceSettings } from '../lib/store';
import type { InstanceCard } from '../lib/types';
import { formatBytes, loaderName } from '../lib/format';

export function InstanceTile({
  card,
  onPlay,
  onStop
}: {
  card: InstanceCard;
  onPlay: (id: string) => void;
  onStop: () => void;
}) {
  const instance = card.instance;
  return (
    <article class={['instance-tile', card.running ? 'instance-tile--running' : ''].filter(Boolean).join(' ')}>
      <button
        type="button"
        class="instance-tile__open"
        onClick={() => openInstanceSettings(instance.id)}
        aria-label={`打开 ${instance.displayName}`}
      >
        <span class="instance-tile__icon">
          <InstanceAvatar card={card} size={64} />
          {card.running ? <span class="instance-tile__live"><span class="live-dot" /></span> : null}
        </span>
        <span class="instance-tile__name" title={instance.displayName}>{instance.displayName}</span>
        <span class="instance-tile__meta">
          {loaderName(instance.loader)} {instance.gameVersion}
        </span>
        <span class="instance-tile__sub">
          {card.sizeBytes > 0 ? formatBytes(card.sizeBytes) : card.playtimeLabel}
        </span>
      </button>
      <div class="instance-tile__actions">
        <button
          type="button"
          class="instance-tile__action"
          aria-label="查看日志"
          title={card.running ? '查看日志' : '实例设置'}
          onClick={() => (card.running ? openConsole('logs') : openInstanceSettings(instance.id))}
        >
          <Icon name={card.running ? 'terminal' : 'settings'} size={13} />
        </button>
        {card.running ? (
          <button type="button" class="instance-tile__action instance-tile__action--stop" aria-label="结束游戏" onClick={onStop}>
            <Icon name="stop" size={13} />
          </button>
        ) : (
          <button
            type="button"
            class="instance-tile__action instance-tile__action--play"
            aria-label={`启动 ${instance.displayName}`}
            onClick={() => onPlay(instance.id)}
          >
            <Icon name="play" size={13} />
          </button>
        )}
      </div>
    </article>
  );
}
