import { api, type UpdateStatus } from '../api';

let current = $state<UpdateStatus | null>(null);
let started = false;

async function refresh() {
  try {
    current = await api.getUpdateStatus();
  } catch {
    /* tolerate transient IPC errors */
  }
}

function ensureStarted() {
  if (started) return;
  started = true;
  // First fetch slightly after startup so the backend has a chance to perform its initial poll.
  setTimeout(refresh, 30_000);
  // Then poll every 5 minutes — backend already runs an hourly check, so this is just a UI catch-up.
  setInterval(refresh, 5 * 60_000);
}

export const updateStatus = {
  get value() {
    ensureStarted();
    return current;
  },
  refresh,
};
