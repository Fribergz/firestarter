type Envelope<T = unknown> = { id: string; type: string; payload?: T };
type Reply<T = unknown> = { id: string; ok: true; result: T } | { id: string; ok: false; error: string };

type PhotinoBridge = {
  sendMessage: (msg: string) => void;
  receiveMessage: (handler: (msg: string) => void) => void;
};

function bridge(): PhotinoBridge | null {
  const w = typeof window === 'undefined' ? null : (window as unknown as { external?: PhotinoBridge });
  const ext = w?.external;
  return ext && typeof ext.sendMessage === 'function' && typeof ext.receiveMessage === 'function' ? ext : null;
}

const pending = new Map<string, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
let initialized = false;

function init() {
  if (initialized) return;
  const b = bridge();
  if (!b) return;
  initialized = true;
  b.receiveMessage((raw: string) => {
    let reply: Reply;
    try {
      reply = JSON.parse(raw) as Reply;
    } catch {
      console.error('[ipc] non-JSON message:', raw);
      return;
    }
    const p = pending.get(reply.id);
    if (!p) return;
    pending.delete(reply.id);
    if (reply.ok) p.resolve(reply.result);
    else p.reject(new Error(reply.error));
  });
}

function randomId() {
  return crypto.randomUUID();
}

export function invoke<TReq, TRes>(type: string, payload?: TReq, timeoutMs = 30000): Promise<TRes> {
  init();
  return new Promise<TRes>((resolve, reject) => {
    const b = bridge();
    if (!b) {
      reject(new Error('Photino bridge not available'));
      return;
    }
    const id = randomId();
    const envelope: Envelope<TReq> = { id, type, payload };
    const timer = window.setTimeout(() => {
      if (pending.delete(id)) reject(new Error(`IPC timeout: ${type}`));
    }, timeoutMs);
    pending.set(id, {
      resolve: (v) => {
        window.clearTimeout(timer);
        resolve(v as TRes);
      },
      reject: (e) => {
        window.clearTimeout(timer);
        reject(e);
      },
    });
    b.sendMessage(JSON.stringify(envelope));
  });
}

export type PingResult = { echo: string; timestamp: string };
export const ping = (message: string) => invoke<{ message: string }, PingResult>('ping', { message });

export type WindowState = { maximized: boolean; minimized: boolean };
export const windowMinimize = () => invoke<undefined, { ok: true }>('window.minimize');
export const windowToggleMaximize = () => invoke<undefined, { maximized: boolean }>('window.toggleMaximize');
export const windowClose = () => invoke<undefined, { ok: true }>('window.close');
/** Hide main window to the system tray (Windows); does not exit the app. */
export const windowHideToTray = () => invoke<undefined, { ok: true }>('window.hideToTray');
export const windowState = () => invoke<undefined, WindowState>('window.state');
export const windowStartDrag = () => invoke<undefined, { ok: true }>('window.startDrag');
export type ResizeEdge = 'top' | 'right' | 'bottom' | 'left' | 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';
export const windowStartResize = (edge: ResizeEdge) =>
  invoke<{ edge: ResizeEdge }, { ok: true }>('window.startResize', { edge });
