export function formatBytes(bytes: number | null | undefined, digits = 1): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(unit === 0 ? 0 : digits)} ${units[unit]}`;
}

export function formatSpeed(bytesPerSecond: number | null | undefined): string {
  if (!bytesPerSecond || bytesPerSecond <= 0) return '';
  return `${formatBytes(bytesPerSecond)}/s`;
}

export function formatCount(value: number): string {
  if (value >= 100000000) return `${(value / 100000000).toFixed(1)} 亿`;
  if (value >= 10000) return `${(value / 10000).toFixed(value >= 100000 ? 0 : 1)} 万`;
  if (value >= 1000) return `${(value / 1000).toFixed(1)}k`;
  return String(value);
}

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return String(iso).slice(0, 10);
  return `${date.getFullYear()}.${String(date.getMonth() + 1).padStart(2, '0')}.${String(date.getDate()).padStart(2, '0')}`;
}

export function formatShortDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return String(iso).slice(0, 10);
  return `${date.getMonth() + 1} 月 ${date.getDate()} 日`;
}

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return '从未启动';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  const diff = Date.now() - date.getTime();
  if (diff < 60000) return '刚刚';
  if (diff < 3600000) return `${Math.floor(diff / 60000)} 分钟前`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)} 小时前`;
  if (diff < 86400000 * 7) return `${Math.floor(diff / 86400000)} 天前`;
  return formatDate(iso);
}

export function formatPlaytime(seconds: number): string {
  if (seconds < 60) return '不到一分钟';
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  if (hours > 0) return `${hours} 小时 ${minutes} 分钟`;
  return `${minutes} 分钟`;
}

export function loaderName(loader: string): string {
  if (loader === 'fabric') return 'Fabric';
  if (loader === 'quilt') return 'Quilt';
  if (loader === 'forge') return 'Forge';
  if (loader === 'neoforge') return 'NeoForge';
  if (loader === 'optifine') return 'OptiFine';
  return '原版';
}

export function projectTypeName(type: string): string {
  if (type === 'resourcepack') return '资源包';
  if (type === 'shader') return '光影';
  if (type === 'mod') return '模组';
  return type;
}

export function formatUserCode(code: string): string {
  return code.toUpperCase();
}

export function todayLabel(): string {
  const date = new Date();
  const weekdays = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];
  return `${date.getMonth() + 1} 月 ${date.getDate()} 日 · ${weekdays[date.getDay()]}`;
}

export function dayNumber(): string {
  const now = new Date();
  const start = new Date(now.getFullYear(), 0, 0);
  return String(Math.floor((now.getTime() - start.getTime()) / 86400000)).padStart(3, '0');
}

export interface LogLevel {
  level: 'error' | 'warn' | 'info' | 'debug' | 'plain';
  text: string;
}

const ANSI = /\x1b\[[0-9;]*m/g;

export function parseLogLine(text: string): LogLevel {
  const clean = text.replace(ANSI, '');
  if (/(?:ERROR|FATAL|\/E\]|Exception|Caused by:)/.test(clean)) return { level: 'error', text: clean };
  if (/(?:WARN|\/W\]|Warning)/.test(clean)) return { level: 'warn', text: clean };
  if (/(?:DEBUG|TRACE)/.test(clean)) return { level: 'debug', text: clean };
  if (/(?:INFO|\/I\])/.test(clean)) return { level: 'info', text: clean };
  return { level: 'plain', text: clean };
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
