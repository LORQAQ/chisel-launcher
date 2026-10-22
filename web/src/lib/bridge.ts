export type BridgeMode = 'webview' | 'mock' | 'none';

interface PendingRequest {
  resolve: (value: any) => void;
  reject: (reason: Error) => void;
  timer: number;
}

interface BridgeErrorShape {
  message?: string;
  code?: string;
}

export class BridgeError extends Error {
  code: string;
  constructor(message: string, code = 'failed') {
    super(message);
    this.name = 'BridgeError';
    this.code = code;
  }
}

const pending = new Map<string, PendingRequest>();
const listeners = new Map<string, Set<(data: any) => void>>();
let mode: BridgeMode = 'none';
let counter = 0;
let mock: { request: (method: string, params?: unknown) => Promise<any> } | null = null;

function webviewApi(): any {
  return (window as any).chrome?.webview;
}

function dispatch(eventName: string, data: any) {
  const exact = listeners.get(eventName);
  if (exact) {
    for (const listener of [...exact]) {
      try {
        listener(data);
      } catch (error) {
        console.error(`event handler failed: ${eventName}`, error);
      }
    }
  }
  const wildcard = listeners.get('*');
  if (wildcard) {
    for (const listener of [...wildcard]) {
      try {
        listener({ event: eventName, data });
      } catch {
        /* ignore */
      }
    }
  }
}

function handleEnvelope(raw: any) {
  let envelope = raw;
  if (typeof raw === 'string') {
    try {
      envelope = JSON.parse(raw);
    } catch {
      return;
    }
  }
  if (!envelope || typeof envelope !== 'object') return;
  if (envelope.type === 'event') {
    dispatch(String(envelope.event ?? ''), envelope.data);
    return;
  }
  if (typeof envelope.id !== 'string') return;
  const entry = pending.get(envelope.id);
  if (!entry) return;
  pending.delete(envelope.id);
  window.clearTimeout(entry.timer);
  if (envelope.ok) entry.resolve(envelope.result);
  else {
    const error = (envelope.error ?? {}) as BridgeErrorShape;
    entry.reject(new BridgeError(error.message || '操作没有完成', error.code));
  }
}

export async function initBridge(): Promise<BridgeMode> {
  if (webviewApi()) {
    mode = 'webview';
    webviewApi().addEventListener('message', (event: MessageEvent) => handleEnvelope(event.data));
    return mode;
  }
  if (import.meta.env.DEV) {
    const module = await import('./mock');
    mock = module.createMock((event, data) => dispatch(event, data));
    mode = 'mock';
    return mode;
  }
  mode = 'none';
  return mode;
}

export function bridgeMode(): BridgeMode {
  return mode;
}

export function isMockMode(): boolean {
  return mode === 'mock';
}

export function request<T = any>(method: string, params?: unknown, timeoutMs = 60000): Promise<T> {
  if (mode === 'mock' && mock) return mock.request(method, params) as Promise<T>;
  if (mode !== 'webview') return Promise.reject(new BridgeError('界面需要在凿境桌面客户端中运行。', 'no_host'));

  const id = `r${Date.now().toString(36)}${(counter++).toString(36)}`;
  return new Promise<T>((resolve, reject) => {
    const timer = window.setTimeout(() => {
      pending.delete(id);
      reject(new BridgeError('等待客户端响应超时，请重试。', 'timeout'));
    }, timeoutMs);
    pending.set(id, { resolve, reject, timer });
    try {
      webviewApi().postMessage({ id, method, params: params ?? {} });
    } catch (error) {
      pending.delete(id);
      window.clearTimeout(timer);
      reject(new BridgeError((error as Error).message || '无法连接客户端', 'post_failed'));
    }
  });
}

export function onEvent(eventName: string, handler: (data: any) => void): () => void {
  let set = listeners.get(eventName);
  if (!set) {
    set = new Set();
    listeners.set(eventName, set);
  }
  set.add(handler);
  return () => {
    set?.delete(handler);
    if (set && set.size === 0) listeners.delete(eventName);
  };
}
