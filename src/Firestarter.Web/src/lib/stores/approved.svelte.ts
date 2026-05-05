/**
 * Session-scoped set of MRs the user just approved from this app.
 * Used so the reviewing list hides them instantly after the user clicks Approve and navigates back —
 * even before the slow per-row GitLab enrichment confirms `approvedByMe` from the server.
 *
 * Cleared when the page is reloaded (which is fine; on the next load the GitLab approval check fills
 * in `approvedByMe` directly).
 */
const approved = new Set<string>();

function key(projectId: number, iid: number): string {
  return `${projectId}/${iid}`;
}

export const approvedMrs = {
  mark(projectId: number, iid: number): void {
    approved.add(key(projectId, iid));
  },
  has(projectId: number, iid: number): boolean {
    return approved.has(key(projectId, iid));
  },
  clear(projectId: number, iid: number): void {
    approved.delete(key(projectId, iid));
  },
};
