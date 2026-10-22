import type { JSX } from 'preact';

export type IconName =
  | 'home'
  | 'layers'
  | 'package'
  | 'settings'
  | 'play'
  | 'stop'
  | 'plus'
  | 'folder'
  | 'trash'
  | 'refresh'
  | 'search'
  | 'download'
  | 'check'
  | 'close'
  | 'minus'
  | 'maximize'
  | 'restore'
  | 'chevronDown'
  | 'chevronRight'
  | 'chevronLeft'
  | 'external'
  | 'monitor'
  | 'cpu'
  | 'memory'
  | 'coffee'
  | 'info'
  | 'alert'
  | 'user'
  | 'shirt'
  | 'news'
  | 'more'
  | 'arrowRight'
  | 'arrowLeft'
  | 'copy'
  | 'terminal'
  | 'globe'
  | 'wrench'
  | 'clock'
  | 'image'
  | 'upload'
  | 'grid'
  | 'list'
  | 'power'
  | 'link'
  | 'box'
  | 'spark'
  | 'pause';

interface IconProps {
  name: IconName;
  size?: number;
  strokeWidth?: number;
  class?: string;
  title?: string;
}

export function Icon({ name, size = 18, strokeWidth = 1.5, class: className, title }: IconProps) {
  const content = paths(name);
  return (
    <svg
      class={className}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width={strokeWidth}
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden={title ? undefined : 'true'}
      role={title ? 'img' : undefined}
    >
      {title ? <title>{title}</title> : null}
      {content}
    </svg>
  );
}

function paths(name: IconName): JSX.Element {
  switch (name) {
    case 'home':
      return (
        <>
          <path d="M3.5 10.9 12 3.8l8.5 7.1V20a.6.6 0 0 1-.6.6h-5.2v-5.5H9.3v5.5H4.1a.6.6 0 0 1-.6-.6Z" />
          <path d="M8.2 4.8V2.9h2.4" opacity=".45" />
        </>
      );
    case 'layers':
      return (
        <>
          <path d="m12 3 9 5.1-9 5.1-9-5.1Z" />
          <path d="m3 12.1 9 5.1 9-5.1" />
          <path d="m3 16.1 9 5.1 9-5.1" opacity=".5" />
        </>
      );
    case 'package':
      return (
        <>
          <path d="M21 8.1 12 3 3 8.1v7.8L12 21l9-5.1Z" />
          <path d="m3 8.1 9 5.1 9-5.1" />
          <path d="M12 13.2V21" />
          <path d="m7.5 5.6 9 5.1v3.4" opacity=".45" />
        </>
      );
    case 'settings':
      return (
        <>
          <path d="M4 7h7M16 7h4" />
          <path d="M4 12h2M11 12h9" />
          <path d="M4 17h9M18 17h2" />
          <rect x="11" y="4.8" width="5" height="4.4" rx="1" />
          <rect x="6" y="9.8" width="5" height="4.4" rx="1" />
          <rect x="13" y="14.8" width="5" height="4.4" rx="1" />
        </>
      );
    case 'play':
      return <path d="M8 4.8 19.4 12 8 19.2Z" fill="currentColor" stroke="none" />;
    case 'stop':
      return <rect x="6.2" y="6.2" width="11.6" height="11.6" rx=".8" fill="currentColor" stroke="none" />;
    case 'pause':
      return (
        <>
          <rect x="6.5" y="5" width="3.6" height="14" rx=".6" fill="currentColor" stroke="none" />
          <rect x="13.9" y="5" width="3.6" height="14" rx=".6" fill="currentColor" stroke="none" />
        </>
      );
    case 'plus':
      return <path d="M12 5v14M5 12h14" strokeWidth="1.7" />;
    case 'folder':
      return <path d="M3 6.8a1 1 0 0 1 1-1h5.1l2.2 2.4H20a1 1 0 0 1 1 1V18a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1Z" />;
    case 'trash':
      return (
        <>
          <path d="M4.5 7h15M9.5 7V4.8h5V7M6.5 7l.9 12.2h9.2L17.5 7" />
          <path d="M10 10.5v5.2M14 10.5v5.2" opacity=".5" />
        </>
      );
    case 'refresh':
      return (
        <>
          <path d="M20 8.5V4.2l-2.4 2.4A8 8 0 1 0 20 13" />
          <path d="M20 4.2h-4.3" />
        </>
      );
    case 'search':
      return (
        <>
          <circle cx="10.8" cy="10.8" r="6.4" />
          <path d="m15.6 15.6 4.2 4.2" />
        </>
      );
    case 'download':
      return (
        <>
          <path d="M12 3.5v11.2M7.4 10.4 12 15l4.6-4.6" />
          <path d="M4 17.5v1.6a1.4 1.4 0 0 0 1.4 1.4h13.2a1.4 1.4 0 0 0 1.4-1.4v-1.6" />
        </>
      );
    case 'upload':
      return (
        <>
          <path d="M12 15.5V4.3M7.4 8.9 12 4.3l4.6 4.6" />
          <path d="M4 17.5v1.6a1.4 1.4 0 0 0 1.4 1.4h13.2a1.4 1.4 0 0 0 1.4-1.4v-1.6" />
        </>
      );
    case 'check':
      return <path d="m4.5 12.4 4.7 4.7L19.5 6.8" strokeWidth="1.8" />;
    case 'close':
      return <path d="m5.5 5.5 13 13M18.5 5.5l-13 13" />;
    case 'minus':
      return <path d="M5 12h14" />;
    case 'maximize':
      return <rect x="5" y="5" width="14" height="14" rx=".7" />;
    case 'restore':
      return (
        <>
          <rect x="4.6" y="8.4" width="10.8" height="10.8" rx=".7" />
          <path d="M8.4 8.4V5.3a.7.7 0 0 1 .7-.7h9.6a.7.7 0 0 1 .7.7v9.6a.7.7 0 0 1-.7.7h-3.1" />
        </>
      );
    case 'chevronDown':
      return <path d="m6 9.3 6 5.7 6-5.7" />;
    case 'chevronRight':
      return <path d="m9.3 6 5.7 6-5.7 6" />;
    case 'chevronLeft':
      return <path d="M14.7 6 9 12l5.7 6" />;
    case 'external':
      return (
        <>
          <path d="M14 4h6v6M20 4l-8.5 8.5" />
          <path d="M19 13.5v5a1.5 1.5 0 0 1-1.5 1.5h-12A1.5 1.5 0 0 1 4 18.5v-12A1.5 1.5 0 0 1 5.5 5h5" />
        </>
      );
    case 'monitor':
      return (
        <>
          <rect x="3" y="4.5" width="18" height="12" rx="1" />
          <path d="M9 20h6M12 16.5V20" />
        </>
      );
    case 'cpu':
      return (
        <>
          <rect x="6.5" y="6.5" width="11" height="11" rx="1" />
          <rect x="10" y="10" width="4" height="4" rx=".4" opacity=".5" />
          <path d="M9 3v3.5M15 3v3.5M9 17.5V21M15 17.5V21M3 9h3.5M3 15h3.5M17.5 9H21M17.5 15H21" opacity=".65" />
        </>
      );
    case 'memory':
      return (
        <>
          <rect x="3" y="8" width="18" height="9" rx="1" />
          <path d="M7 17v2.5M12 17v2.5M17 17v2.5M7 11h2M11 11h2M15 11h2" opacity=".7" />
        </>
      );
    case 'coffee':
      return (
        <>
          <path d="M5 9h11v5.2a4.8 4.8 0 0 1-4.8 4.8H9.8A4.8 4.8 0 0 1 5 14.2Z" />
          <path d="M16 10.3h1.6a2.2 2.2 0 0 1 0 4.4H16" />
          <path d="M8.5 3.5c-.8 1 .8 1.6 0 2.6M12 3.5c-.8 1 .8 1.6 0 2.6" opacity=".55" />
        </>
      );
    case 'info':
      return (
        <>
          <circle cx="12" cy="12" r="8.5" />
          <path d="M12 11v5.5" />
          <circle cx="12" cy="7.8" r=".9" fill="currentColor" stroke="none" />
        </>
      );
    case 'alert':
      return (
        <>
          <path d="M12 3.6 21 20H3Z" />
          <path d="M12 9v5" />
          <circle cx="12" cy="17.2" r=".9" fill="currentColor" stroke="none" />
        </>
      );
    case 'user':
      return (
        <>
          <circle cx="12" cy="8" r="4" />
          <path d="M4.5 20.5c.7-4 3.8-6.2 7.5-6.2s6.8 2.2 7.5 6.2" />
        </>
      );
    case 'shirt':
      return (
        <>
          <path d="m8.8 4.2-4.6 2.6 2 4.2 2.1-1v10h7.4v-10l2.1 1 2-4.2-4.6-2.6a3.5 3.5 0 0 1-6.4 0Z" />
        </>
      );
    case 'news':
      return (
        <>
          <path d="M4 5.5h13v14H5.5A1.5 1.5 0 0 1 4 18Z" />
          <path d="M17 8.5h3v9a2 2 0 0 1-2 2" />
          <path d="M7.5 9h6M7.5 12h6M7.5 15h4" opacity=".7" />
        </>
      );
    case 'more':
      return (
        <>
          <circle cx="5.5" cy="12" r="1.2" fill="currentColor" stroke="none" />
          <circle cx="12" cy="12" r="1.2" fill="currentColor" stroke="none" />
          <circle cx="18.5" cy="12" r="1.2" fill="currentColor" stroke="none" />
        </>
      );
    case 'arrowRight':
      return <path d="M4 12h15M13.5 6.5 20 12l-6.5 5.5" />;
    case 'arrowLeft':
      return <path d="M20 12H5M10.5 6.5 4 12l6.5 5.5" />;
    case 'copy':
      return (
        <>
          <rect x="8" y="8" width="11" height="11" rx="1" />
          <path d="M16 8V5.5A1.5 1.5 0 0 0 14.5 4h-9A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16H8" />
        </>
      );
    case 'terminal':
      return (
        <>
          <rect x="3" y="4.5" width="18" height="15" rx="1.2" />
          <path d="m7.2 9 3 3-3 3M13 15h4" />
        </>
      );
    case 'globe':
      return (
        <>
          <circle cx="12" cy="12" r="8.5" />
          <path d="M3.5 12h17M12 3.5c2.4 2.3 3.6 5.1 3.6 8.5S14.4 18.2 12 20.5c-2.4-2.3-3.6-5.1-3.6-8.5S9.6 5.8 12 3.5Z" />
        </>
      );
    case 'wrench':
      return (
        <>
          <path d="M14.5 6.2a4.2 4.2 0 0 0 5.3 5.3l-9 9a2.4 2.4 0 0 1-3.4-3.4l8.2-8.2" />
          <path d="M14.5 6.2 17 3.7a4.2 4.2 0 0 1 3.4 7.6" opacity=".6" />
        </>
      );
    case 'clock':
      return (
        <>
          <circle cx="12" cy="12" r="8.5" />
          <path d="M12 7.5V12l3 2" />
        </>
      );
    case 'image':
      return (
        <>
          <rect x="3.5" y="5" width="17" height="14" rx="1" />
          <circle cx="9" cy="10" r="1.6" />
          <path d="m5.5 17 4.7-4.5 3.2 3 2.2-2 3.4 3.5" />
        </>
      );
    case 'grid':
      return (
        <>
          <rect x="4" y="4" width="6.5" height="6.5" rx=".6" />
          <rect x="13.5" y="4" width="6.5" height="6.5" rx=".6" />
          <rect x="4" y="13.5" width="6.5" height="6.5" rx=".6" />
          <rect x="13.5" y="13.5" width="6.5" height="6.5" rx=".6" />
        </>
      );
    case 'list':
      return (
        <>
          <path d="M9 6.5h11M9 12h11M9 17.5h11" />
          <circle cx="4.6" cy="6.5" r=".9" fill="currentColor" stroke="none" />
          <circle cx="4.6" cy="12" r=".9" fill="currentColor" stroke="none" />
          <circle cx="4.6" cy="17.5" r=".9" fill="currentColor" stroke="none" />
        </>
      );
    case 'power':
      return (
        <>
          <path d="M12 3.5V11" />
          <path d="M7.2 6.4a7 7 0 1 0 9.6 0" />
        </>
      );
    case 'link':
      return (
        <>
          <path d="M9.5 14.5 14.5 9.5" />
          <path d="M8 12.5 5.8 14.7a3.4 3.4 0 0 0 4.8 4.8l2.2-2.2" />
          <path d="m16 11.5 2.2-2.2a3.4 3.4 0 0 0-4.8-4.8l-2.2 2.2" />
        </>
      );
    case 'box':
      return (
        <>
          <rect x="4" y="4" width="16" height="16" rx="1" />
          <path d="m4 9.5 8 4 8-4M12 13.5V20" opacity=".7" />
        </>
      );
    case 'spark':
      return (
        <>
          <path d="M12 3.5 13.8 9l5.7 1.8-5.7 1.8L12 18.2l-1.8-5.6L4.5 10.8 10.2 9Z" />
          <path d="M18.5 16.5v4M16.5 18.5h4" opacity=".55" />
        </>
      );
    default:
      return <circle cx="12" cy="12" r="7" />;
  }
}

export function BrandMark({ size = 24, class: className }: { size?: number; class?: string }) {
  return (
    <img
      src="/loaders/vanilla.png"
      width={size}
      height={size}
      class={['brand-block', className].filter(Boolean).join(' ')}
      alt=""
      draggable={false}
    />
  );
}
