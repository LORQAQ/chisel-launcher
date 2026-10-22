import { useEffect, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import type { InstanceCard } from '../lib/types';
import { LoaderTile } from './LoaderTile';

const cache = new Map<string, string | null>();

export function InstanceAvatar({ card, size = 64, class: className = '' }: { card: InstanceCard; size?: number; class?: string }) {
  const version = card.iconVersion ?? 0;
  const key = `${card.instance.id}:${version}`;
  const [dataUrl, setDataUrl] = useState<string | null>(() => cache.get(key) ?? null);

  useEffect(() => {
    let alive = true;
    if (!card.hasIcon || version === 0) {
      setDataUrl(null);
      return;
    }
    const cached = cache.get(key);
    if (cached !== undefined) {
      setDataUrl(cached);
      return;
    }
    void request<{ dataUrl: string | null }>('instance.icon', { id: card.instance.id }, 15000)
      .then((result) => {
        const value = result?.dataUrl ?? null;
        if (cache.size > 64) cache.clear();
        cache.set(key, value);
        if (alive) setDataUrl(value);
      })
      .catch(() => {
        cache.set(key, null);
        if (alive) setDataUrl(null);
      });
    return () => {
      alive = false;
    };
  }, [key, card.hasIcon, card.instance.id, version]);

  if (dataUrl) {
    return (
      <span class={['instance-avatar', className].filter(Boolean).join(' ')} style={{ width: `${size}px`, height: `${size}px` }}>
        <img src={dataUrl} alt="" draggable={false} />
      </span>
    );
  }
  return <LoaderTile loader={card.instance.loader} size={size} />;
}
