export type RouteName =
  | 'start'
  | 'projects'
  | 'project'
  | 'project-pipelines'
  | 'pipelines-cache'
  | 'mr'
  | 'mr-mine'
  | 'mr-reviewer'
  | 'sync'
  | 'extensions'
  | 'settings'
  | 'stats';

export type ProjectDetailView = 'overview' | 'branches' | 'merge-requests';

/** Project sub-screen: built-in `ProjectDetailView` or an extension tab id (`ext-{id}`). */
export type ProjectRouteView = ProjectDetailView | string;

export type Route =
  | { name: 'start' }
  | { name: 'projects' }
  | { name: 'project'; projectId: number; view?: ProjectRouteView }
  | { name: 'project-pipelines'; projectId: number }
  | { name: 'pipelines-cache' }
  | { name: 'mr'; projectId: number; mrId: number; listParent: 'project' | 'reviewing' }
  | { name: 'mr-mine' }
  | { name: 'mr-reviewer' }
  | { name: 'sync' }
  | { name: 'extensions' }
  | { name: 'settings' }
  | { name: 'stats' };

let current = $state<Route>({ name: 'start' });

export const route = {
  get current() {
    return current;
  },
  navigate(to: Route) {
    current = to;
  },
  goStart() {
    current = { name: 'start' };
  },
  goProjects() {
    current = { name: 'projects' };
  },
  goProject(projectId: number, view?: ProjectRouteView) {
    current = { name: 'project', projectId, view };
  },
  /** Full Jenkins pipeline list for a project (up to API cap, currently 50). */
  goProjectPipelines(projectId: number) {
    current = { name: 'project-pipelines', projectId };
  },
  /** All projects’ pipeline rows read from the local DB cache (no live Jenkins). */
  goPipelinesCache() {
    current = { name: 'pipelines-cache' };
  },
  /** After applying `view` in ProjectDetail, clear it so the URL state does not re-assert the tab. */
  consumeProjectView() {
    if (current.name !== 'project' || !current.view) return;
    const { projectId } = current;
    current = { name: 'project', projectId };
  },
  goMr(projectId: number, mrId: number, listParent: 'project' | 'reviewing' = 'project') {
    current = { name: 'mr', projectId, mrId, listParent };
  },
  goMrMine() {
    current = { name: 'mr-mine' };
  },
  goMrReviewer() {
    current = { name: 'mr-reviewer' };
  },
  goSync() {
    current = { name: 'sync' };
  },
  goExtensions() {
    current = { name: 'extensions' };
  },
  goSettings() {
    current = { name: 'settings' };
  },
  goStats() {
    current = { name: 'stats' };
  },
};
