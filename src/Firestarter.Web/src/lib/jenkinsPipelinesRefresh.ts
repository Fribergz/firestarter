/** Throttle live Jenkins API fetches from the app (per project) — stored in localStorage. */

const STORAGE_KEY_PREFIX = 'firestarter.jenkinsPipelinesLiveAt.';

const MIN_INTERVAL_MS = 60 * 60 * 1000; // 1 hour

function key(projectId: number): string {
  return STORAGE_KEY_PREFIX + projectId;
}

export function getJenkinsPipelinesLiveFetchedAt(projectId: number): number | null {
  try {
    const v = localStorage.getItem(key(projectId));
    if (v == null) return null;
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  } catch {
    return null;
  }
}

/** Whether a *live* `listProjectPipelines` call is allowed (auto refresh). Manual refresh bypasses this. */
export function shouldAutoFetchJenkinsPipelinesFromLive(projectId: number, now = Date.now()): boolean {
  const t = getJenkinsPipelinesLiveFetchedAt(projectId);
  if (t === null) return true;
  return now - t >= MIN_INTERVAL_MS;
}

export function recordJenkinsPipelinesLiveFetch(projectId: number, at = Date.now()): void {
  try {
    localStorage.setItem(key(projectId), String(at));
  } catch {
    /* ignore */
  }
}
