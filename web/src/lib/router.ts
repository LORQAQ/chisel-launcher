import { signal } from '@preact/signals';
import type { PageId } from './types';

export const ROUTES: PageId[] = ['home', 'versions', 'content', 'settings'];

function parseHash(): PageId {
  const value = location.hash.replace(/^#\/?/, '').split('?')[0];
  return (ROUTES.includes(value as PageId) ? value : 'home') as PageId;
}

export const route = signal<PageId>(parseHash());

window.addEventListener('hashchange', () => {
  route.value = parseHash();
});

export function navigate(page: PageId) {
  if (route.value === page) return;
  if (location.hash !== `#/${page}`) location.hash = `/${page}`;
  else route.value = page;
}

export const NAV_ITEMS: Array<{ id: PageId; label: string; latin: string }> = [
  { id: 'home', label: '首页', latin: 'Home' },
  { id: 'versions', label: '实例', latin: 'Instances' },
  { id: 'content', label: '内容', latin: 'Content' },
  { id: 'settings', label: '设置', latin: 'Settings' }
];
