const LOADERS: Record<string, { label: string; accent: string; logo?: string; letter: string }> = {
  vanilla: { label: '原版', accent: '#5d8f4f', logo: '/loaders/vanilla.png', letter: 'V' },
  fabric: { label: 'Fabric', accent: '#c8a98e', logo: '/loaders/fabric.png', letter: 'F' },
  quilt: { label: 'Quilt', accent: '#9b6fd4', logo: '/loaders/quilt.svg', letter: 'Q' },
  forge: { label: 'Forge', accent: '#d68a4c', logo: '/loaders/forge.svg', letter: 'G' },
  neoforge: { label: 'NeoForge', accent: '#e0703e', letter: 'N' },
  optifine: { label: 'OptiFine', accent: '#8c9bb5', letter: 'O' }
};

export function loaderMeta(loader: string) {
  return LOADERS[loader] ?? LOADERS.vanilla;
}

export function LoaderTile({
  loader,
  size = 48,
  class: className = '',
  label
}: {
  loader: string;
  size?: number;
  class?: string;
  label?: string;
}) {
  const meta = loaderMeta(loader);
  return (
    <span
      class={['loader-tile', className].filter(Boolean).join(' ')}
      style={{
        width: `${size}px`,
        height: `${size}px`,
        '--loader-accent': meta.accent,
        fontSize: `${Math.max(12, Math.round(size * 0.36))}px`
      }}
      aria-hidden={label ? undefined : 'true'}
      aria-label={label}
    >
      {meta.logo ? (
        <img src={meta.logo} alt="" draggable={false} />
      ) : (
        <b>{meta.letter}</b>
      )}
    </span>
  );
}
