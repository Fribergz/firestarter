import { invoke } from './ipc';

/** Jenkins list + per-build enrichment can exceed the default 30s IPC timeout when `take` is large. */
const PROJECT_PIPELINES_LIST_IPC_TIMEOUT_MS = 120_000;

/** Default matches Core extension process cap when manifest omits `timeoutSeconds` (600). */
const EXTENSION_RUN_IPC_FALLBACK_MS = 630_000;

/** IPC wait must exceed `ExtensionRunner`'s process timeout plus a small buffer for exit/log flush. */
export function extensionRunIpcTimeoutMs(timeoutSeconds: number): number {
  const sec =
    typeof timeoutSeconds === 'number' && Number.isFinite(timeoutSeconds) && timeoutSeconds > 0
      ? timeoutSeconds
      : 600;
  return sec * 1000 + 45_000;
}

export type GitlabConfig = {
  baseUrl: string | null;
  hasPat: boolean;
  syncIntervalSeconds: number;
  currentUsername: string | null;
};

export type GitlabConfigUpdate = {
  baseUrl: string | null;
  pat: string | null;
  clearPat: boolean;
  syncIntervalSeconds: number;
};

export type JenkinsConfig = {
  baseUrl: string | null;
  username: string | null;
  hasApiToken: boolean;
};

export type JenkinsConfigUpdate = {
  baseUrl: string | null;
  username: string | null;
  apiToken: string | null;
  clearApiToken: boolean;
};

export type SyncScope = 'FullProjects' | 'Project';

export type SyncStartRequest = {
  scope?: SyncScope;
  projectId?: number | null;
  reason?: string | null;
};

export type SyncStartResult = {
  enqueued: boolean;
  scope: string;
};

export type ProjectSearchHit = {
  id: number;
  gitlabId: number;
  pathWithNamespace: string;
  name: string;
  description: string | null;
  defaultBranch: string | null;
  webUrl: string;
  archived: boolean;
  lastActivityAt: string | null;
};

export type ProjectSearchResult = {
  hits: ProjectSearchHit[];
};

export type SyncStatus = {
  state: 'Idle' | 'Running' | 'Error';
  currentScope: string | null;
  currentItem: string | null;
  processed: number;
  total: number | null;
  lastStartedAt: string | null;
  lastFinishedAt: string | null;
  lastError: string | null;
  queueDepth: number;
};

export const api = {
  ping: (message: string) => invoke<{ message: string }, { echo: string; timestamp: string }>('ping', { message }),
  getSettings: () => invoke<undefined, GitlabConfig>('settings.get'),
  updateSettings: (update: GitlabConfigUpdate) => invoke<GitlabConfigUpdate, GitlabConfig>('settings.update', update),
  getJenkinsConfig: () => invoke<undefined, JenkinsConfig>('jenkins.get'),
  updateJenkins: (update: JenkinsConfigUpdate) =>
    invoke<JenkinsConfigUpdate, JenkinsConfig>('jenkins.update', update),
  testJenkins: () => invoke<undefined, { ok: true }>('jenkins.test'),
  setProjectJenkinsJob: (projectId: number, jobPath: string | null) =>
    invoke<{ projectId: number; jobPath: string | null }, { ok: true; project: ProjectDetail }>(
      'jenkins.projectJob.set',
      { projectId, jobPath },
    ),
  getJenkinsBuildStatus: (projectId: number) =>
    invoke<{ projectId: number }, JenkinsBuildStatus>('jenkins.build.status', { projectId }),
  triggerJenkinsBuild: (projectId: number, opts?: { branch?: string | null; mrIid?: number | null }) =>
    invoke<{ projectId: number; branch?: string | null; mrIid?: number | null }, { ok: true }>(
      'jenkins.build.trigger',
      { projectId, branch: opts?.branch ?? null, mrIid: opts?.mrIid ?? null },
    ),
  startSync: (request: SyncStartRequest = {}) => invoke<SyncStartRequest, SyncStartResult>('sync.start', request),
  getSyncStatus: () => invoke<undefined, SyncStatus>('sync.status'),
  resetJenkinsJobPaths: () => invoke<undefined, { reset: number }>('sync.resetJenkinsJobPaths'),
  getUpdateStatus: () => invoke<undefined, UpdateStatus>('update.status'),
  applyUpdate: () => invoke<undefined, { ok: boolean; error: string | null }>('update.apply'),
  searchProjects: (query: string, limit = 25) =>
    invoke<{ query: string; limit: number }, ProjectSearchResult>('projects.search', { query, limit }),
  openProject: (projectId: number, branch?: string | null, ideId?: number | null) =>
    invoke<{ projectId: number; branch?: string | null; ideId?: number | null }, OpenProjectResult>('projects.open', {
      projectId,
      branch: branch ?? null,
      ideId: ideId ?? null,
    }),
  openProjectExplorer: (projectId: number, branch?: string | null) =>
    invoke<{ projectId: number; branch?: string | null }, { ok: true; repositoryPath: string }>(
      'projects.openExplorer',
      { projectId, branch: branch ?? null },
    ),
  openProjectTerminal: (projectId: number, branch?: string | null) =>
    invoke<{ projectId: number; branch?: string | null }, { ok: true; repositoryPath: string }>(
      'projects.openTerminal',
      { projectId, branch: branch ?? null },
    ),
  listIdes: () => invoke<undefined, { ides: IdeRegistration[] }>('ide.list'),
  upsertIde: (ide: IdeRegistrationUpsert) => invoke<IdeRegistrationUpsert, IdeRegistration>('ide.upsert', ide),
  deleteIde: (id: number) => invoke<{ id: number }, { deleted: boolean }>('ide.delete', { id }),
  getWorkspace: () => invoke<undefined, WorkspaceConfig>('workspace.get'),
  updateWorkspace: (cfg: WorkspaceConfig) => invoke<WorkspaceConfig, WorkspaceConfig>('workspace.update', cfg),
  getExtensionsRoot: () => invoke<undefined, { root: string | null }>('extensions.root.get'),
  setExtensionsRoot: (root: string | null) => invoke<{ root: string | null }, { root: string | null }>('extensions.root.set', { root }),
  scanExtensions: () => invoke<undefined, ExtensionScanResult>('extensions.scan'),
  listExtensions: () => invoke<undefined, { extensions: ExtensionView[] }>('extensions.list'),
  setExtensionEnabled: (id: number, enabled: boolean) =>
    invoke<{ id: number; enabled: boolean }, { extension: ExtensionView }>('extensions.setEnabled', { id, enabled }),
  setExtensionSettings: (id: number, values: Record<string, string | null>) =>
    invoke<{ id: number; values: Record<string, string | null> }, { extension: ExtensionView }>(
      'extensions.setSettings',
      { id, values },
    ),
  runExtension: (req: ExtensionRunRequest, ipcTimeoutMs = EXTENSION_RUN_IPC_FALLBACK_MS) =>
    invoke<ExtensionRunRequest, ExtensionRunResult>('extensions.run', req, ipcTimeoutMs),
  listRuns: (req: { extensionId?: number | null; projectId?: number | null; take?: number } = {}) =>
    invoke<{ extensionId?: number | null; projectId?: number | null; take?: number }, { runs: ExtensionRunSummary[] }>('extensions.runs', req),
  getRunLog: (runId: number) =>
    invoke<{ runId: number }, ExtensionRunLog | null>('extensions.runLog', { runId }),
  getUi: () => invoke<undefined, { theme: string }>('ui.get'),
  setUiTheme: (theme: string) => invoke<{ theme: string }, { theme: string }>('ui.setTheme', { theme }),
  listProjects: (filter?: string | null, take = 200) =>
    invoke<{ filter?: string | null; take?: number }, { projects: ProjectListItem[] }>('projects.list', { filter: filter ?? null, take }),
  getProject: (projectId: number) =>
    invoke<{ projectId: number }, { project: ProjectDetail } | null>('projects.get', { projectId }),
  /** `take` is clamped server-side to 1–50. */
  listProjectPipelines: (projectId: number, take = 10) =>
    invoke<
      { projectId: number; take?: number },
      { pipelines: JenkinsPipelineItem[]; jenkinsConfigured: boolean; error: string | null; cachedAt?: string | null }
    >('projects.pipelines.list', { projectId, take }, PROJECT_PIPELINES_LIST_IPC_TIMEOUT_MS),
  /** DB-backed snapshots only (projects with a non-empty pipeline cache). */
  listCachedProjectPipelines: () =>
    invoke<undefined, { items: CachedProjectPipelines[] }>('projects.pipelines.cached'),
  getMergeRequest: (projectId: number, iid: number) =>
    invoke<{ projectId: number; iid: number }, { mr: MergeRequestDto } | null>('mr.get', { projectId, iid }),
  getMrOverview: (projectId: number, iid: number) =>
    invoke<{ projectId: number; iid: number }, { overview: MergeRequestOverview } | null>('mr.overview.get', { projectId, iid }),
  getMrCommits: (projectId: number, iid: number) =>
    invoke<{ projectId: number; iid: number }, { commits: MergeRequestCommit[] }>('mr.commits.get', { projectId, iid }),
  getMrChanges: (projectId: number, iid: number) =>
    invoke<{ projectId: number; iid: number }, { changes: MergeRequestFileChange[] }>('mr.changes.get', { projectId, iid }),
  getMrDiscussions: (projectId: number, iid: number) =>
    invoke<{ projectId: number; iid: number }, { discussions: MergeRequestDiscussion[] }>('mr.discussions.get', { projectId, iid }),
  approveMr: (projectId: number, iid: number) =>
    invoke<{ projectId: number; iid: number }, { approvals: MergeRequestApprovals } | null>('mr.approve', { projectId, iid }),
  postMrDiscussion: (projectId: number, iid: number, body: string) =>
    invoke<{ projectId: number; iid: number; body: string }, { id: string | null; webUrl: string | null }>(
      'mr.discussion.create',
      { projectId, iid, body },
    ),
  dotnetInteractiveRunStart: (req: { projectId: number; branch: string | null; targetPath: string | null }) =>
    invoke<typeof req, { ok: boolean; error?: string | null; pid?: number | null }>(
      'dotnet.interactiveRun.start',
      req,
    ),
  dotnetInteractiveRunStop: () =>
    invoke<undefined, { ok: boolean; error?: string | null }>('dotnet.interactiveRun.stop'),
  dotnetInteractiveRunStatus: () =>
    invoke<undefined, { running: boolean; pid?: number | null }>('dotnet.interactiveRun.status'),
  listMyMrs: () =>
    invoke<undefined, { items: MergeRequestListItem[] }>('mr.listMine'),
  listReviewerMrs: () =>
    invoke<undefined, { items: MergeRequestListItem[] }>('mr.listReviewer'),
  markProjectVisited: (projectId: number) =>
    invoke<{ projectId: number }, { ok: boolean }>('projects.markVisited', { projectId }),
  setProjectStarred: (projectId: number, starred: boolean) =>
    invoke<{ projectId: number; starred: boolean }, { ok: boolean }>('projects.setStarred', { projectId, starred }),
  getCounters: () =>
    invoke<undefined, CountersDto>('counters'),
  getStatsSummary: () => invoke<undefined, ApiCallSummary>('stats.summary'),
  listStats: (req: { take?: number; from?: string; to?: string } = {}) =>
    invoke<typeof req, { entries: ApiCallEntry[] }>('stats.list', req),
};

export type OpenProjectResult = {
  repositoryPath: string;
  branch: string;
  commitSha: string | null;
  ideName: string;
  ideProcessId: number;
};

export type IdeRegistration = {
  id: number;
  name: string;
  executablePath: string;
  argTemplate: string;
  isDefault: boolean;
};

export type IdeRegistrationUpsert = {
  id?: number | null;
  name: string;
  executablePath: string;
  argTemplate: string;
  isDefault: boolean;
};

export type WorkspaceConfig = {
  projectsRoot: string;
  tempRoot: string;
};

export type ExtensionParameter = {
  name: string;
  description: string | null;
  default: string | null;
  required: boolean;
};

export type ExtensionTarget = {
  view: string;
  label: string | null;
};

export type ExtensionSettingType = 'string' | 'boolean' | 'multi-string';

export type ExtensionSetting = {
  name: string;
  label: string | null;
  description: string | null;
  type: ExtensionSettingType;
  default: string | null;
};

export type ExtensionView = {
  id: number;
  name: string;
  description: string | null;
  scriptPath: string;
  timeoutSeconds: number;
  isEnabled: boolean;
  parameters: ExtensionParameter[];
  targets: ExtensionTarget[];
  settingsSchema: ExtensionSetting[];
  /** Resolved values (declared default overlaid by stored value), keyed by setting name. Multi-string is comma-joined. */
  settingsValues: Record<string, string>;
};

export type ExtensionScanResult = {
  added: number;
  updated: number;
  removed: number;
  errors: string[];
};

export type ExtensionRunRequest = {
  extensionId: number;
  projectId: number;
  branch?: string | null;
  parameters?: Record<string, string>;
};

export type ExtensionRunResult = {
  id: number;
  status: string;
  exitCode: number | null;
  startedAt: string;
  finishedAt: string | null;
  statsJson: string | null;
  errorMessage: string | null;
  stdoutPath: string | null;
  stderrPath: string | null;
};

export type ProjectListItem = {
  id: number;
  gitlabId: number;
  pathWithNamespace: string;
  name: string;
  description: string | null;
  defaultBranch: string | null;
  webUrl: string;
  lastActivityAt: string | null;
  archived: boolean;
  starred: boolean;
  openMergeRequestCount: number;
  branchCount: number;
};

export type BranchDto = {
  id: number;
  name: string;
  sha: string;
  isDefault: boolean;
  isProtected: boolean;
  updatedAt: string;
};

export type MergeRequestDto = {
  id: number;
  iid: number;
  title: string;
  state: string;
  sourceBranch: string;
  targetBranch: string;
  authorUsername: string | null;
  assigneeUsernames: string | null;
  reviewerUsernames: string | null;
  webUrl: string;
  draft: boolean;
  createdAt: string;
  updatedAt: string;
};

export type MergeRequestApprovals = {
  approved: boolean;
  approvalsRequired: number;
  approvalsLeft: number;
  userHasApproved: boolean;
  userCanApprove: boolean;
  approvedBy: string[];
  suggestedApprovers: string[];
};

export type MergeRequestOverview = {
  description: string | null;
  userNotesCount: number;
  changesCount: string;
  divergedCommitsCount: number | null;
  labels: string[];
  baseSha: string | null;
  headSha: string | null;
  startSha: string | null;
  approvals: MergeRequestApprovals | null;
};

export type MergeRequestCommit = {
  id: string;
  shortId: string;
  title: string;
  message: string | null;
  authorName: string | null;
  authorEmail: string | null;
  authoredDate: string;
  webUrl: string | null;
};

export type MergeRequestFileChange = {
  oldPath: string;
  newPath: string;
  newFile: boolean;
  deletedFile: boolean;
  renamedFile: boolean;
  diff: string;
};

export type MergeRequestNote = {
  id: number;
  author: string | null;
  body: string | null;
  createdAt: string;
  updatedAt: string;
  system: boolean;
  resolved: boolean;
  resolvable: boolean;
};

export type MergeRequestDiscussion = {
  id: string;
  individualNote: boolean;
  notes: MergeRequestNote[];
};

export type MergeRequestListItem = {
  id: number;
  iid: number;
  projectId: number;
  projectPath: string;
  title: string;
  state: string;
  sourceBranch: string;
  targetBranch: string;
  authorUsername: string | null;
  assigneeUsernames: string | null;
  reviewerUsernames: string | null;
  webUrl: string;
  draft: boolean;
  createdAt: string;
  updatedAt: string;
  /** Unresolved (open) discussion threads, live from GitLab; null if unavailable. */
  openDiscussions?: number | null;
  /** Open threads with at least two user notes; null if unavailable. */
};

export type JenkinsPipelineStepItem = {
  name: string;
  status: string;
};

/** Recent Jenkins builds for the project job (shown as “pipelines” in the UI). */
export type JenkinsPipelineItem = {
  number: number;
  status: string;
  url: string;
  sha: string | null;
  /** From build parameters (e.g. TAG_NAME, VERSION) when exposed by the job. */
  versionTag: string | null;
  /** Declarative / Pipeline stage flow nodes from <code>wfapi/describe</code>, when available. */
  steps: JenkinsPipelineStepItem[] | null;
  /** First stage or step that failed, aborted, was not built, or was unstable. */
  failedOn: string | null;
  startedAt: string | null;
};

/** One project’s rows from <code>projects.pipelines.cached</code> (local DB only). */
export type CachedProjectPipelines = {
  projectId: number;
  pathWithNamespace: string;
  cachedAt: string | null;
  pipelines: JenkinsPipelineItem[];
};

export type ProjectDetail = {
  id: number;
  gitlabId: number;
  pathWithNamespace: string;
  name: string;
  description: string | null;
  defaultBranch: string | null;
  webUrl: string;
  /** Slash-separated Jenkins job path (e.g. `folder/job-name`). */
  jenkinsJobPath: string | null;
  lastActivityAt: string | null;
  /** When branches + open MRs were last fetched from GitLab (for coalescing sync.start). */
  branchesMrsSyncedAt?: string | null;
  archived: boolean;
  branches: BranchDto[];
  openMergeRequests: MergeRequestDto[];
  /** From DB: Jenkins URL, token, and this project’s job path are set. */
  jenkinsPipelinesConfigured?: boolean;
  /** Last successful pipeline list persisted in the DB (stale-while-revalidate). */
  jenkinsPipelinesCache?: JenkinsPipelineItem[] | null;
  jenkinsPipelinesCachedAt?: string | null;
};

export type JenkinsLastBuild = {
  number: number;
  url: string;
  building: boolean;
  result: string | null;
};

export type JenkinsBuildStatus = {
  configured: boolean;
  jobUrl: string | null;
  lastBuild: JenkinsLastBuild | null;
  error: string | null;
};

export type CountersDto = {
  authoredOrAssignedOpen: number;
  reviewerOpen: number;
  projects: number;
  branches: number;
};

export type ExtensionRunLog = {
  id: number;
  stdout: string | null;
  stderr: string | null;
  stdoutTruncated: boolean;
  stderrTruncated: boolean;
};

export type ExtensionRunSummary = {
  id: number;
  extensionId: number;
  extensionName: string;
  projectId: number;
  projectPath: string;
  branch: string;
  commitSha: string | null;
  status: string;
  exitCode: number | null;
  startedAt: string;
  finishedAt: string | null;
  statsJson: string | null;
  errorMessage: string | null;
};

export type ApiCallEntry = {
  id: number;
  timestamp: string;
  method: string;
  host: string;
  path: string;
  statusCode: number;
  durationMs: number;
  requestBytes: number;
  responseBytes: number;
  source: string;
  errorMessage: string | null;
};

export type ApiCallDay = {
  /** YYYY-MM-DD (UTC) */
  date: string;
  countsBySource: Record<string, number>;
  total: number;
};

export type ApiCallSummary = {
  totalLast7d: number;
  totalToday: number;
  failuresLast7d: number;
  averageDurationMsLast7d: number;
  byDay: ApiCallDay[];
  sources: string[];
};

export type UpdateStatus = {
  currentVersion: string;
  updateAvailable: boolean;
  latestVersion: string | null;
  downloadUrl: string | null;
  lastError: string | null;
  lastCheckedAt: string | null;
  applyInProgress: boolean;
};
