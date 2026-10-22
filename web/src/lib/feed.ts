import { request } from './bridge';
import type { DailyBlock, NewsItem } from './types';

export interface NewsFeed {
  news: NewsItem[];
  patches: NewsItem[];
  at: number;
}

const NEWS_TTL = 10 * 60 * 1000;
let cached: NewsFeed | null = null;
let inflight: Promise<NewsFeed> | null = null;

export function peekNews(): NewsFeed | null {
  return cached;
}

export async function loadNews(force = false): Promise<NewsFeed> {
  if (!force && cached && Date.now() - cached.at < NEWS_TTL) return cached;
  if (inflight) return inflight;

  inflight = request<{ news: NewsItem[]; patches: NewsItem[] }>(
    'news.list',
    { force },
    force ? 45000 : 30000
  )
    .then((payload) => {
      const feed: NewsFeed = {
        news: payload?.news ?? [],
        patches: payload?.patches ?? [],
        at: Date.now()
      };
      if (feed.news.length > 0 || feed.patches.length > 0) cached = feed;
      return feed;
    })
    .catch((error) => {
      if (cached) return cached;
      throw error;
    })
    .finally(() => {
      inflight = null;
    });

  return inflight;
}

let dailyKey = '';
let daily: DailyBlock | null = null;
let dailyInflight: Promise<DailyBlock | null> | null = null;

function dayKey(instanceId?: string | null): string {
  const now = new Date();
  return `${now.getFullYear()}-${now.getMonth()}-${now.getDate()}:${instanceId ?? ''}`;
}

export function peekDaily(instanceId?: string | null): DailyBlock | null {
  return dailyKey === dayKey(instanceId) ? daily : null;
}

export async function loadDaily(instanceId?: string | null): Promise<DailyBlock | null> {
  const key = dayKey(instanceId);
  if (daily && dailyKey === key) return daily;
  if (dailyInflight) return dailyInflight;

  dailyInflight = request<DailyBlock>('block.daily', { instanceId: instanceId ?? undefined }, 30000)
    .then((block) => {
      if (block) {
        daily = block;
        dailyKey = key;
      }
      return block;
    })
    .finally(() => {
      dailyInflight = null;
    });

  return dailyInflight;
}
