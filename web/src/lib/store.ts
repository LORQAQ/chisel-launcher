import { computed, signal } from '@preact/signals';
import { BridgeError, onEvent, request } from './bridge';
import type {
  AccountView,
  AccountsPayload,
  AppSettings,
  Bootstrap,
  ContentJob,
  ContentProgress,
  CrashDiagnosis,
  DeviceCodeInfo,
  GameLogLine,
  GameStatus,
  InstallProgress,
  InstanceSizesPayload,
  JavaProgress,
  LaunchState,
  MemoryInfo,
  ToastItem,
  ToastTone
} from './types';

export const DEFAULT_SETTINGS: AppSettings = {
  schema: 1,
  onboarded: false,
  lastInstanceId: null,
  activeAccountId: null,
  downloadSource: 'auto',
  downloadConcurrency: 16,
  autoMemory: true,
  memoryMb: null,
  javaPath: null,
  autoDownloadJava: true,
  closeOnLaunch: false,
  showConsoleOnLaunch: true,
  customResolution: false,
  width: 1280,
  height: 720,
  jvmArgs: '',
  microsoftClientId: '',
  curseForgeApiKey: '',
  windowWidth: 1360,
  windowHeight: 860,
  windowMaximized: false
};

interface InstallState {
  jobId: string;
  progress: InstallProgress | null;
  state: 'running' | 'done' | 'failed' | 'cancelled';
  instanceId?: string;
  message?: string;
  gameVersion?: string;
  loader?: string;
  loaderVersion?: string | null;
  displayName?: string | null;
}

interface AuthState {
  phase: 'idle' | 'starting' | 'waiting' | 'success' | 'error';
  message?: string;
  code?: DeviceCodeInfo;
  error?: string;
  account?: AccountView;
}

export const ready = signal(false);
export const bootError = signal<string | null>(null);
export const bootstrap = signal<Bootstrap | null>(null);
export const settings = signal<AppSettings>({ ...DEFAULT_SETTINGS });
export const instances = signal<Bootstrap['instances']>([]);
export const accounts = signal<AccountsPayload>({ items: [], activeId: null });
export const memoryInfo = signal<MemoryInfo>({ totalMb: 0, availableMb: 0, recommendedMb: 4096, defaultMb: 4096 });
export const systemInfo = signal<Bootstrap['system'] | null>(null);
export const appInfo = signal<Bootstrap['app'] | null>(null);

export const running = signal<LaunchState>({ running: false, instanceId: null, status: null });
export const launchStatus = signal<GameStatus | null>(null);
export const lastDiagnosis = signal<CrashDiagnosis | null>(null);
export const consoleOpen = signal(false);
export const consoleTab = signal<'logs' | 'diagnosis'>('logs');
export const logs = signal<GameLogLine[]>([]);
export const windowMaximized = signal(false);

export const sizesReady = signal(false);
export const installJob = signal<InstallState | null>(null);
export const contentJobs = signal<Record<string, ContentJob>>({});
export const contentRevision = signal(0);
export const javaProgress = signal<JavaProgress | null>(null);
export const shaderProgress = signal<{ jobId: string; message: string; stage: string } | null>(null);
export const authState = signal<AuthState>({ phase: 'idle' });
export const accountSheetOpen = signal(false);
export const installSheetOpen = signal(false);
export const modpackSheetOpen = signal(false);
export const instanceSheetId = signal<string | null>(null);
export const toasts = signal<ToastItem[]>([]);

export const activeAccount = computed<AccountView | null>(
  () => accounts.value.items.find((account) => account.isActive) ?? accounts.value.items[0] ?? null
);

export const featuredInstance = computed(() => {
  const list = instances.value;
  if (list.length === 0) return null;
  const preferred = settings.value.lastInstanceId;
  return (
    list.find((card) => card.instance.id === preferred) ??
    list.find((card) => card.instance.id === running.value.instanceId) ??
    list[0]
  );
});

export const runningInstance = computed(() => {
  const id = running.value.instanceId;
  if (!id) return null;
  return instances.value.find((card) => card.instance.id === id) ?? null;
});

export const totalPlaySeconds = computed(() =>
  instances.value.reduce((total, card) => total + (card.instance.playSeconds || 0), 0)
);

let toastSeed = 0;
let wired = false;
let bootStarted = false;
let sizesAt = 0;
let sizesInflight: Promise<void> | null = null;
let sizesTimer = 0;

export function showToast(text: string, tone: ToastTone = 'info', detail?: string) {
  const item: ToastItem = { id: ++toastSeed, text, tone, detail };
  toasts.value = [...toasts.value, item].slice(-4);
  window.setTimeout(() => {
    toasts.value = toasts.value.filter((toast) => toast.id !== item.id);
  }, tone === 'error' ? 6200 : 3800);
}

export function dismissToast(id: number) {
  toasts.value = toasts.value.filter((toast) => toast.id !== id);
}

export function errorMessage(error: unknown): string {
  if (error instanceof BridgeError) return error.message;
  if (error instanceof Error) return error.message;
  return String(error ?? '操作没有完成');
}

export function openInstallSheet() {
  installSheetOpen.value = true;
}

export function closeInstallSheet() {
  installSheetOpen.value = false;
}

export function openModpackSheet() {
  modpackSheetOpen.value = true;
}

export function closeModpackSheet() {
  modpackSheetOpen.value = false;
}

export function openInstanceSettings(id: string) {
  instanceSheetId.value = id;
}

export function closeInstanceSettings() {
  instanceSheetId.value = null;
}

export function openConsole(tab: 'logs' | 'diagnosis' = 'logs') {
  consoleTab.value = tab;
  consoleOpen.value = true;
}

export function closeConsole() {
  consoleOpen.value = false;
}

export function setConsoleTab(tab: 'logs' | 'diagnosis') {
  consoleTab.value = tab;
}

function subscribeEvents() {
  if (wired) return;
  wired = true;

  onEvent('instances.changed', () => {
    void refreshInstances();
    scheduleSizeRefresh();
  });

  onEvent('settings.changed', (data: AppSettings) => {
    if (data && typeof data === 'object') settings.value = data;
  });

  onEvent('accounts.changed', (data: AccountsPayload) => {
    if (data?.items) {
      accounts.value = data;
      settings.value = { ...settings.value, activeAccountId: data.activeId };
    }
  });

  onEvent('window.state', (data: { maximized: boolean }) => {
    windowMaximized.value = Boolean(data?.maximized);
  });

  onEvent('install.progress', (progress: InstallProgress) => {
    if (!progress) return;
    const previous = installJob.value;
    installJob.value = {
      jobId: progress.jobId,
      progress,
      state: progress.done ? 'done' : previous?.state === 'failed' ? 'failed' : 'running',
      instanceId: progress.instanceId ?? previous?.instanceId,
      gameVersion: previous?.gameVersion,
      loader: previous?.loader,
      loaderVersion: previous?.loaderVersion,
      displayName: previous?.displayName
    };
  });

  onEvent('install.done', (data: { jobId: string; instanceId: string }) => {
    const previous = installJob.value;
    installJob.value = {
      jobId: data.jobId,
      progress: previous?.progress ?? null,
      state: 'done',
      instanceId: data.instanceId,
      gameVersion: previous?.gameVersion,
      loader: previous?.loader,
      loaderVersion: previous?.loaderVersion,
      displayName: previous?.displayName
    };
    showToast('实例已创建。', 'success');
    void refreshInstances();
    scheduleSizeRefresh();
  });

  onEvent('install.failed', (data: { jobId: string; message: string }) => {
    const previous = installJob.value;
    installJob.value = {
      jobId: data.jobId,
      progress: previous?.progress ?? null,
      state: 'failed',
      message: data.message,
      instanceId: previous?.instanceId,
      gameVersion: previous?.gameVersion,
      loader: previous?.loader,
      loaderVersion: previous?.loaderVersion,
      displayName: previous?.displayName
    };
    showToast('安装没有完成', 'error', data.message);
  });

  onEvent('install.cancelled', (data: { jobId: string }) => {
    const previous = installJob.value;
    installJob.value = {
      jobId: data.jobId,
      progress: previous?.progress ?? null,
      state: 'cancelled',
      instanceId: previous?.instanceId,
      gameVersion: previous?.gameVersion,
      loader: previous?.loader,
      loaderVersion: previous?.loaderVersion,
      displayName: previous?.displayName
    };
    showToast('已取消安装。', 'info');
  });

  onEvent('launch.status', (status: GameStatus) => {
    if (!status) return;
    launchStatus.value = status;
    const active = status.state !== 'exited' && status.state !== 'failed';
    running.value = { running: active, instanceId: active ? status.instanceId : null, status };
    if (status.diagnosis) {
      lastDiagnosis.value = status.diagnosis;
      openConsole('diagnosis');
    }
    if (status.state === 'failed') {
      showToast('游戏异常退出', 'error', status.message);
      void refreshInstances();
    }
    if (status.state === 'exited') {
      void refreshInstances();
    }
    if (status.state === 'running') {
      void refreshInstances();
    }
  });

  onEvent('launch.logs', (data: { instanceId: string; lines: GameLogLine[] }) => {
    if (!data?.lines?.length) return;
    const next = logs.value.concat(data.lines);
    logs.value = next.length > 1800 ? next.slice(next.length - 1800) : next;
  });

  onEvent('launch.started', () => {
    void refreshInstances();
  });

  onEvent('content.changed', () => {
    contentRevision.value++;
  });

  onEvent('content.progress', (data: { requestId: string; progress: ContentProgress }) => {
    if (!data?.requestId) return;
    const jobs = { ...contentJobs.value };
    const existing = jobs[data.requestId];
    jobs[data.requestId] = {
      requestId: data.requestId,
      projectId: existing?.projectId ?? '',
      title: existing?.title || data.progress?.projectTitle || '正在安装',
      instanceId: existing?.instanceId ?? '',
      progress: data.progress,
      state: 'running'
    };
    contentJobs.value = jobs;
  });

  onEvent('content.done', (data: { requestId: string; result: any; instanceId: string }) => {
    if (!data?.requestId) return;
    const jobs = { ...contentJobs.value };
    const existing = jobs[data.requestId];
    jobs[data.requestId] = {
      requestId: data.requestId,
      projectId: existing?.projectId ?? '',
      title: existing?.title || data.result?.installed?.[0]?.title || '内容',
      instanceId: data.instanceId ?? existing?.instanceId ?? '',
      progress: existing?.progress ?? null,
      state: 'done',
      result: data.result
    };
    contentJobs.value = jobs;
    contentRevision.value++;
    const title = data.result?.installed?.[0]?.title;
    showToast(title ? `${title} 已放好。` : '内容已安装。', 'success');
    void refreshInstances();
    scheduleSizeRefresh();
    window.setTimeout(() => pruneContentJob(data.requestId), 8000);
  });

  onEvent('content.failed', (data: { requestId: string; message: string }) => {
    if (!data?.requestId) return;
    const jobs = { ...contentJobs.value };
    const existing = jobs[data.requestId];
    jobs[data.requestId] = {
      requestId: data.requestId,
      projectId: existing?.projectId ?? '',
      title: existing?.title ?? '内容',
      instanceId: existing?.instanceId ?? '',
      progress: existing?.progress ?? null,
      state: 'failed',
      message: data.message
    };
    contentJobs.value = jobs;
    showToast('内容没有装好', 'error', data.message);
    window.setTimeout(() => pruneContentJob(data.requestId), 7000);
  });

  onEvent('content.cancelled', (data: { requestId: string }) => {
    const jobs = { ...contentJobs.value };
    if (jobs[data.requestId]) {
      jobs[data.requestId] = { ...jobs[data.requestId], state: 'cancelled' };
      contentJobs.value = jobs;
    }
    window.setTimeout(() => pruneContentJob(data.requestId), 3000);
  });

  onEvent('java.progress', (progress: JavaProgress) => {
    javaProgress.value = progress;
  });

  onEvent('java.done', (runtime: { major: number }) => {
    javaProgress.value = null;
    showToast(`Java ${runtime?.major ?? ''} 已就绪。`, 'success');
  });

  onEvent('java.failed', (data: { message: string }) => {
    javaProgress.value = null;
    showToast('Java 下载失败', 'error', data.message);
  });

  onEvent('java.cancelled', () => {
    javaProgress.value = null;
    showToast('已停下 Java 准备。', 'info');
  });

  onEvent('shader.progress', (data: { jobId: string; stage: string; message: string }) => {
    if (data?.jobId) shaderProgress.value = data;
  });

  onEvent('shader.done', (data: { kind: string; version?: string }) => {
    shaderProgress.value = null;
    contentRevision.value++;
    showToast(`${data?.kind === 'optifine' ? 'OptiFine' : 'Iris'} 已安装。`, 'success');
    void refreshInstances();
  });

  onEvent('shader.failed', (data: { message: string }) => {
    shaderProgress.value = null;
    showToast('光影组件没有装好', 'error', data.message);
  });

  onEvent('shader.cancelled', () => {
    shaderProgress.value = null;
  });

  onEvent('auth.waiting', (data: { message: string }) => {
    authState.value = { ...authState.value, phase: 'waiting', message: data.message };
  });

  onEvent('auth.complete', (data: { account: AccountView; accounts: AccountsPayload }) => {
    authState.value = { phase: 'success', account: data.account };
    accounts.value = data.accounts;
    settings.value = { ...settings.value, activeAccountId: data.accounts.activeId };
    showToast(`已登录为 ${data.account.name}。`, 'success');
  });

  onEvent('auth.failed', (data: { message: string }) => {
    authState.value = { ...authState.value, phase: 'error', error: data.message };
  });

  onEvent('auth.cancelled', () => {
    authState.value = { phase: 'idle' };
  });

  onEvent('webview.failed', (data: { reason: string }) => {
    showToast('界面进程重新载入', 'warn', data?.reason);
  });
}

export function pruneContentJob(requestId: string) {
  const jobs = { ...contentJobs.value };
  delete jobs[requestId];
  contentJobs.value = jobs;
}

export async function boot() {
  if (bootStarted) return;
  bootStarted = true;
  subscribeEvents();
  try {
    const data = await request<Bootstrap>('app.bootstrap', {}, 30000);
    bootstrap.value = data;
    settings.value = data.settings;
    instances.value = data.instances ?? [];
    accounts.value = data.accounts ?? { items: [], activeId: null };
    memoryInfo.value = data.memory;
    systemInfo.value = data.system;
    appInfo.value = data.app;
    windowMaximized.value = Boolean(data.window?.maximized);
    running.value = data.running ?? { running: false, instanceId: null, status: null };
    launchStatus.value = data.running?.status ?? null;
    if (data.running?.status?.diagnosis) {
      lastDiagnosis.value = data.running.status.diagnosis;
    }
    ready.value = true;
    const schedule = (window as Window & { requestIdleCallback?: (cb: () => void, options?: { timeout: number }) => number }).requestIdleCallback;
    if (schedule) schedule(() => void refreshSizes(), { timeout: 4000 });
    else window.setTimeout(() => void refreshSizes(), 2500);
  } catch (error) {
    bootError.value = errorMessage(error);
  }
}

export async function refreshInstances() {
  try {
    const list = await request<Bootstrap['instances']>('instance.list');
    const known = new Map(instances.value.map((card) => [card.instance.id, card.sizeBytes]));
    instances.value = (list ?? []).map((card) => ({
      ...card,
      sizeBytes: card.sizeBytes || known.get(card.instance.id) || 0
    }));
  } catch {
    /* the UI keeps the previous list */
  }
}

export async function refreshSizes(force = false): Promise<void> {
  if (!force && sizesReady.value && Date.now() - sizesAt < 60000) return;
  if (sizesInflight) return sizesInflight;

  sizesInflight = request<InstanceSizesPayload>('instance.sizes', {}, 90000)
    .then((payload) => {
      const sizes = payload?.sizes ?? {};
      instances.value = instances.value.map((card) => ({
        ...card,
        sizeBytes: sizes[card.instance.id] ?? card.sizeBytes
      }));
      sizesAt = Date.now();
      sizesReady.value = true;
    })
    .catch(() => {
      /* sizes are decoration; the previous numbers stay */
    })
    .finally(() => {
      sizesInflight = null;
    });

  return sizesInflight;
}

function scheduleSizeRefresh() {
  window.clearTimeout(sizesTimer);
  sizesTimer = window.setTimeout(() => void refreshSizes(true), 1200);
}

export function applyAccounts(payload: AccountsPayload) {
  accounts.value = payload;
  settings.value = { ...settings.value, activeAccountId: payload.activeId };
}

export async function updateSettings(patch: Partial<AppSettings>): Promise<AppSettings | null> {
  settings.value = { ...settings.value, ...patch };
  try {
    const updated = await request<AppSettings>('settings.update', patch);
    if (updated) settings.value = updated;
    return updated;
  } catch (error) {
    showToast('设置没有保存', 'error', errorMessage(error));
    try {
      const snapshot = await request<AppSettings>('settings.snapshot');
      if (snapshot) settings.value = snapshot;
    } catch {
      /* ignore */
    }
    return null;
  }
}

export async function launchGame(instanceId: string) {
  if (!instanceId) return;
  if (running.value.running) {
    openConsole('logs');
    return;
  }
  logs.value = [];
  launchStatus.value = null;
  lastDiagnosis.value = null;
  if (settings.value.showConsoleOnLaunch) openConsole('logs');
  try {
    await request('launch.start', { instanceId });
  } catch (error) {
    showToast('无法启动游戏', 'error', errorMessage(error));
  }
}

export async function stopGame() {
  try {
    await request('launch.stop');
  } catch (error) {
    showToast('无法结束游戏进程', 'error', errorMessage(error));
  }
}

export async function copyText(text: string) {
  if (!text) return;
  try {
    const result = await request<{ copied: boolean }>('clipboard.write', { text }, 15000);
    if (!result?.copied) throw new Error('copy failed');
    showToast('已复制到剪贴板。', 'success');
  } catch {
    try {
      await navigator.clipboard.writeText(text);
      showToast('已复制到剪贴板。', 'success');
    } catch {
      showToast('复制失败', 'error');
    }
  }
}

export async function startInstallation(params: {
  gameVersion: string;
  loader: string;
  loaderVersion?: string | null;
  displayName?: string | null;
  modpackPath?: string | null;
}) {
  if (installJob.value?.state === 'running') {
    showToast('已经有安装任务在进行中。', 'warn');
    return null;
  }
  try {
    const result = await request<{ jobId: string }>('instance.install', params, 30000);
    const current = installJob.value;
    if (!current || current.jobId !== result.jobId) {
      installJob.value = {
        jobId: result.jobId,
        progress: null,
        state: 'running',
        gameVersion: params.gameVersion,
        loader: params.loader,
        loaderVersion: params.loaderVersion ?? null,
        displayName: params.displayName ?? null
      };
    }
    return result.jobId;
  } catch (error) {
    showToast('无法开始安装', 'error', errorMessage(error));
    return null;
  }
}

export async function cancelInstallation() {
  try {
    await request('install.cancel');
  } catch (error) {
    showToast('无法取消安装', 'error', errorMessage(error));
  }
}

export async function registerContentJob(
  requestId: string,
  info: { projectId: string; title: string; instanceId: string }
) {
  const jobs = { ...contentJobs.value };
  const existing = jobs[requestId];
  jobs[requestId] = {
    requestId,
    projectId: info.projectId,
    title: info.title,
    instanceId: info.instanceId,
    progress: existing?.progress ?? null,
    state: existing?.state ?? 'running',
    result: existing?.result
  };
  contentJobs.value = jobs;
}

export async function cancelContentJob(requestId: string) {
  try {
    await request('content.cancel', { id: requestId });
  } catch (error) {
    showToast('无法取消下载', 'error', errorMessage(error));
  }
}

export async function beginMicrosoftAuth(): Promise<DeviceCodeInfo | null> {
  authState.value = { phase: 'starting' };
  try {
    const code = await request<DeviceCodeInfo>('accounts.microsoft.begin', {}, 40000);
    authState.value = { phase: 'waiting', code, message: '正在等待浏览器登录' };
    return code;
  } catch (error) {
    authState.value = { phase: 'error', error: errorMessage(error) };
    return null;
  }
}

export async function cancelMicrosoftAuth() {
  try {
    await request('accounts.microsoft.cancel');
  } catch {
    /* ignore */
  }
  authState.value = { phase: 'idle' };
}

export function resetAuthState() {
  authState.value = { phase: 'idle' };
}
