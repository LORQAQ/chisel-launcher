import { useEffect, useState } from 'preact/hooks';
import { request } from '../lib/bridge';
import { loadNews, peekNews } from '../lib/feed';
import {
  formatBytes,
  formatDate,
  loaderName,
  relativeTime
} from '../lib/format';
import { navigate } from '../lib/router';
import {
  accountSheetOpen,
  accounts,
  activeAccount,
  appInfo,
  errorMessage,
  instances,
  launchGame,
  openConsole,
  openInstanceSettings,
  openInstallSheet,
  running,
  showToast,
  stopGame
} from '../lib/store';
import type { InstanceCard, NewsItem, NoteItem } from '../lib/types';
import { Icon } from '../components/Icon';
import { InstanceTile } from '../components/InstanceTile';
import { InstanceAvatar } from '../components/InstanceAvatar';
import { Button, Empty } from '../components/ui';
import { SkinFace } from '../components/Skin';

export function HomePage() {
  const cards = instances.value;
  const account = activeAccount.value;
  const [news, setNews] = useState<NewsItem[]>(() => peekNews()?.news ?? []);
  const [patches, setPatches] = useState<NewsItem[]>(() => peekNews()?.patches ?? []);
  const [refreshing, setRefreshing] = useState(false);
  const [notes, setNotes] = useState<NoteItem[]>([]);
  const [noteText, setNoteText] = useState('');
  const [noteBusy, setNoteBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    void loadNews()
      .then((feed) => {
        if (!alive) return;
        setNews(feed.news);
        setPatches(feed.patches);
      })
      .catch(() => {});
    void request<{ notes: NoteItem[] }>('notes.list', {}, 15000)
      .then((payload) => {
        if (alive) setNotes(payload?.notes ?? []);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  const refreshNews = async () => {
    if (refreshing) return;
    setRefreshing(true);
    try {
      const feed = await loadNews(true);
      setNews(feed.news);
      setPatches(feed.patches);
      showToast('新闻已刷新', 'success');
    } catch (error) {
      showToast('没有取到新闻', 'error', errorMessage(error));
    } finally {
      setRefreshing(false);
    }
  };

  const leaveNote = async () => {
    const text = noteText.trim();
    if (!text || noteBusy) return;
    setNoteBusy(true);
    try {
      const note = await request<NoteItem>('notes.add', { text }, 15000);
      if (note) {
        setNotes((current) => [note, ...current].slice(0, 12));
        setNoteText((current) => (current.trim() === text ? '' : current));
      }
    } catch (error) {
      showToast('留言没有发出去', 'error', errorMessage(error));
    } finally {
      setNoteBusy(false);
    }
  };

  const removeNote = async (id: string) => {
    try {
      const result = await request<{ removed: boolean }>('notes.remove', { id }, 15000);
      if (result?.removed) setNotes((current) => current.filter((note) => note.id !== id));
    } catch (error) {
      showToast('没能删除留言', 'error', errorMessage(error));
    }
  };

  const recent = [...cards]
    .sort((a, b) => {
      const left = a.instance.lastPlayedAt ?? a.instance.createdAt;
      const right = b.instance.lastPlayedAt ?? b.instance.createdAt;
      return new Date(right).getTime() - new Date(left).getTime();
    })
    .slice(0, 3);

  const totalBytes = cards.reduce((sum, card) => sum + (card.sizeBytes || 0), 0);

  return (
    <div class="page home">
      <header class="home-head">
        <div>
          <h1 class="page-title">首页</h1>
          <p class="page-sub">
            {cards.length > 0
              ? `${cards.length} 个实例 · ${formatBytes(totalBytes)} · ${accounts.value.items.length} 个账号`
              : '还没有实例。选一个版本，几分钟后就能进入游戏。'}
          </p>
        </div>
        <div class="page-head__actions">
          <Button variant="primary" icon="plus" onClick={openInstallSheet}>
            新建实例
          </Button>
        </div>
      </header>

      <div class="home-grid">
        <main class="home-main">
          {recent.length > 0 ? (
            <section class="home-section">
              <div class="section-head">
                <div>
                  <h2>继续游玩</h2>
                  <p>最近启动过的实例</p>
                </div>
                <button type="button" class="text-link" onClick={() => navigate('versions')}>
                  全部实例 <Icon name="chevronRight" size={13} />
                </button>
              </div>
              <div class="jump-list">
                {recent.map((card, index) => (
                  <JumpRow key={card.instance.id} card={card} primary={index === 0} />
                ))}
              </div>
            </section>
          ) : null}

          <section class="home-section">
            <div class="section-head">
              <div>
                <h2>我的实例</h2>
                <p>每个实例都有独立的存档、模组和配置</p>
              </div>
              <div class="section-head__actions">
                <button type="button" class="icon-text-btn" onClick={() => navigate('versions')}>
                  <Icon name="grid" size={14} /> 管理
                </button>
                <Button size="sm" variant="primary" icon="plus" onClick={openInstallSheet}>
                  新建
                </Button>
              </div>
            </div>
            {cards.length === 0 ? (
              <Empty
                icon="grid"
                title="还没有实例"
                text="安装一个原版、Fabric、Forge 或整合包，就能开始。"
                boxed
              >
                <Button variant="primary" icon="plus" onClick={openInstallSheet}>
                  新建实例
                </Button>
              </Empty>
            ) : (
              <div class="instance-grid-tiles">
                {cards.map((card) => (
                  <InstanceTile
                    key={card.instance.id}
                    card={card}
                    onPlay={(id) => {
                      if (running.value.running) openConsole('logs');
                      else void launchGame(id);
                    }}
                    onStop={() => void stopGame()}
                  />
                ))}
                <button type="button" class="instance-tile instance-tile--add" onClick={openInstallSheet}>
                  <span class="instance-tile__icon"><Icon name="plus" size={24} /></span>
                  <span class="instance-tile__name">新建实例</span>
                  <span class="instance-tile__meta">选择版本与加载器</span>
                </button>
              </div>
            )}
          </section>

          <section class="home-section">
            <div class="section-head">
              <div>
                <h2>社区新闻</h2>
                <p>来自 Minecraft 与 Modrinth 的最新消息</p>
              </div>
              <button type="button" class="text-link" onClick={() => void refreshNews()} disabled={refreshing}>
                <Icon name="refresh" size={13} class={refreshing ? 'spin' : undefined} />
                {refreshing ? '刷新中' : '刷新'}
              </button>
            </div>
            {news.length === 0 ? (
              <div class="home-empty-line">暂时没有新闻。</div>
            ) : (
              <div class="news-feed">
                <button
                  type="button"
                  class="news-lead"
                  onClick={() => news[0].link && request('shell.openUrl', { url: news[0].link })}
                >
                  <span class="news-lead__media">
                    {news[0].imageUrl ? (
                      <img src={news[0].imageUrl} alt="" loading="lazy" decoding="async" />
                    ) : (
                      <span class="news-lead__blank"><Icon name="news" size={26} /></span>
                    )}
                  </span>
                  <span class="news-lead__body">
                    <span class="news-meta">
                      {news[0].category ?? '新闻'} · {formatDate(news[0].date)}
                    </span>
                    <span class="news-lead__title">{news[0].title}</span>
                    <span class="news-lead__text">{news[0].text}</span>
                  </span>
                </button>
                <div class="news-list">
                  {news.slice(1, 5).map((item) => (
                    <button
                      type="button"
                      class="news-row"
                      key={item.title}
                      onClick={() => item.link && request('shell.openUrl', { url: item.link })}
                    >
                      <span class="news-row__thumb">
                        {item.imageUrl ? (
                          <img src={item.imageUrl} alt="" loading="lazy" decoding="async" />
                        ) : (
                          <Icon name="news" size={15} />
                        )}
                      </span>
                      <span class="news-row__body">
                        <span class="news-row__title">{item.title}</span>
                        <span class="news-meta">{item.category ?? '新闻'} · {formatDate(item.date)}</span>
                      </span>
                      <Icon name="chevronRight" size={14} class="news-row__chevron" />
                    </button>
                  ))}
                </div>
              </div>
            )}
          </section>
        </main>

        <aside class="home-side">
          <section class="side-card account-side">
            <div class="side-card__head">
              <span>账号</span>
              <button type="button" class="text-link" onClick={() => { accountSheetOpen.value = true; }}>
                切换
              </button>
            </div>
            <button type="button" class="account-side__main" onClick={() => { accountSheetOpen.value = true; }}>
              <span class="account-side__face">
                {account ? <SkinFace account={account} /> : <Icon name="user" size={20} />}
              </span>
              <span class="account-side__text">
                <b>{account?.name ?? '未登录'}</b>
                <span>
                  {account
                    ? account.type === 'microsoft'
                      ? 'Microsoft 账号'
                      : account.type === 'authlib'
                        ? '外置登录'
                        : '离线账号'
                    : '点击添加账号'}
                </span>
              </span>
              <Icon name="chevronRight" size={14} />
            </button>
          </section>

          <section class="side-card">
            <div class="side-card__head">
              <span>留言</span>
              <span class="mono faint">{notes.length}</span>
            </div>
            <form
              class="note-form"
              onSubmit={(event) => {
                event.preventDefault();
                void leaveNote();
              }}
            >
              <textarea
                class="note-form__input"
                value={noteText}
                maxLength={80}
                rows={2}
                placeholder="说点什么…"
                aria-label="留言"
                onInput={(event) => setNoteText((event.target as HTMLTextAreaElement).value)}
                onKeyDown={(event) => {
                  if (event.isComposing || event.keyCode === 229) return;
                  if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    void leaveNote();
                  }
                }}
              />
              <div class="note-form__foot">
                <span class="note-form__count mono">{noteText.length}/80</span>
                <button type="submit" class="note-form__send" disabled={!noteText.trim() || noteBusy}>
                  {noteBusy ? '发送中' : '发送'}
                </button>
              </div>
            </form>
            {notes.length === 0 ? (
              <div class="home-empty-line">还没有留言。</div>
            ) : (
              <div class="note-list">
                {notes.slice(0, 2).map((note) => (
                  <article class={['note', note.rare ? 'note--rare' : ''].filter(Boolean).join(' ')} key={note.id}>
                    <p class="note__text">{note.text}</p>
                    <p class="note__echo">{note.reply}</p>
                    <div class="note__foot">
                      <span class="mono">{relativeTime(note.createdAt)}</span>
                      <button
                        type="button"
                        class="note__remove"
                        aria-label="删除留言"
                        onClick={() => void removeNote(note.id)}
                      >
                        <Icon name="close" size={10} />
                      </button>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>

          <section class="side-card">
            <div class="side-card__head">
              <span>版本动态</span>
              <span class="mono faint">{patches.length}</span>
            </div>
            {patches.length === 0 ? (
              <div class="home-empty-line">暂时没有新版本。</div>
            ) : (
              <div class="patch-list">
                {patches.slice(0, 4).map((patch) => (
                  <div class="patch" key={patch.title}>
                    <div class="patch__date mono">{formatDate(patch.date)}</div>
                    <div class="patch__title">{patch.title}</div>
                  </div>
                ))}
              </div>
            )}
          </section>

          <div class="side-foot">
            <span>凿境 {appInfo.value?.version ?? '0.1.0'}</span>
            <span>非官方启动器</span>
          </div>
        </aside>
      </div>
    </div>
  );
}

function JumpRow({ card, primary }: { card: InstanceCard; primary: boolean }) {
  const instance = card.instance;
  const isRunning = running.value.running && running.value.instanceId === instance.id;
  return (
    <div class={['jump-row', primary ? 'jump-row--primary' : ''].filter(Boolean).join(' ')}>
      <button
        type="button"
        class="jump-row__main"
        onClick={() => openInstanceSettings(instance.id)}
        aria-label={`打开 ${instance.displayName}`}
      >
        <InstanceAvatar card={card} size={44} />
        <span class="jump-row__body">
          <span class="jump-row__name">{instance.displayName}</span>
          <span class="jump-row__meta">
            {loaderName(instance.loader)} · {instance.gameVersion}
            {instance.loaderVersion ? ` · ${instance.loaderVersion}` : ''}
            <span class="dot-sep">·</span>
            {instance.lastPlayedAt ? relativeTime(instance.lastPlayedAt) : '从未启动'}
          </span>
        </span>
      </button>
      <span class={['jump-row__state', isRunning ? 'is-live' : ''].filter(Boolean).join(' ')}>
        {isRunning ? '运行中' : card.playtimeLabel}
      </span>
      {isRunning ? (
        <button type="button" class="jump-row__play jump-row__play--stop" aria-label="结束游戏" onClick={() => void stopGame()}>
          <Icon name="stop" size={16} />
        </button>
      ) : (
        <button
          type="button"
          class="jump-row__play"
          aria-label={`启动 ${instance.displayName}`}
          onClick={() => {
            if (running.value.running) openConsole('logs');
            else void launchGame(instance.id);
          }}
        >
          <Icon name="play" size={16} />
          <span>启动</span>
        </button>
      )}
    </div>
  );
}
