/** Skip enqueueing a GitLab project-scope sync when branches/MRs were synced within this window. */
export const GITLAB_PROJECT_BRANCH_MR_SYNC_FRESH_MS = 120_000;

export function isProjectBranchMrSyncFresh(branchesMrsSyncedAt: string | null | undefined): boolean {
  if (!branchesMrsSyncedAt) return false;
  const t = new Date(branchesMrsSyncedAt).getTime();
  if (Number.isNaN(t)) return false;
  return Date.now() - t < GITLAB_PROJECT_BRANCH_MR_SYNC_FRESH_MS;
}
