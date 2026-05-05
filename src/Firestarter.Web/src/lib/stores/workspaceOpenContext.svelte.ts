export type WorkspaceOpenTarget = { projectId: number; branch: string | null };

let target = $state<WorkspaceOpenTarget | null>(null);

/**
 * When set, the workspace FAB (clone + IDE / Explorer / Terminal) uses this project and branch.
 * Routes with a project context update this; cleanup on navigation by returning from `$effect`.
 */
export const workspaceOpen = {
  get current() {
    return target;
  },
  set(value: WorkspaceOpenTarget | null) {
    target = value;
  },
};
