import { signal } from '@preact/signals';
import { request } from './bridge';
import type { AccountView } from './types';

const cache = new Map<string, string | null>();
const inflight = new Map<string, Promise<string | null>>();

export const skinRevision = signal(0);

export function invalidateSkin(accountId: string) {
  cache.delete(accountId);
  inflight.delete(accountId);
  skinRevision.value++;
}

export async function loadSkin(account: AccountView | null): Promise<string | null> {
  if (!account || !account.hasSkin) return null;
  if (cache.has(account.id)) return cache.get(account.id) ?? null;
  const current = inflight.get(account.id);
  if (current) return current;
  const job = request<{ accountId: string; dataUrl: string | null }>('accounts.skin', { id: account.id }, 20000)
    .then((payload) => {
      const value = payload?.dataUrl || null;
      cache.set(account.id, value);
      return value;
    })
    .catch(() => {
      cache.set(account.id, null);
      return null;
    })
    .finally(() => inflight.delete(account.id));
  inflight.set(account.id, job);
  return job;
}
