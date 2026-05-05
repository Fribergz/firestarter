<script lang="ts">
  import {
    api,
    extensionRunIpcTimeoutMs,
    type ProjectDetail,
    type ExtensionView,
    type ExtensionRunSummary,
    type JenkinsPipelineItem,
  } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { workspaceOpen } from '../lib/stores/workspaceOpenContext.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import { kpiNumeralFontWeightCss } from '../lib/theme/chrome';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import ProjectViewTabs from '../lib/components/ProjectViewTabs.svelte';
  import JenkinsPipelineList from '../lib/components/JenkinsPipelineList.svelte';
  import Pill from '../lib/components/Pill.svelte';
  import DotnetReviewResult, { type DotnetReviewStats } from '../lib/components/DotnetReviewResult.svelte';
  import ExtensionRunLogViewer from '../lib/components/ExtensionRunLogViewer.svelte';
  import { timeAgo, splitPath } from '../lib/format';
  import { isProjectBranchMrSyncFresh } from '../lib/gitlabSyncFreshness';
  import {
    recordJenkinsPipelinesLiveFetch,
    shouldAutoFetchJenkinsPipelinesFromLive,
  } from '../lib/jenkinsPipelinesRefresh';

  interface Props { projectId: number }
  let { projectId }: Props = $props();

  let project = $state<ProjectDetail | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let tab = $state<string>('overview');

  let projectExtensions = $state<ExtensionView[]>([]);
  let runsByExt = $state<Record<number, ExtensionRunSummary | null>>({});
  let runningExtId = $state<number | null>(null);
  let runErrors = $state<Record<number, string | null>>({});
  let showOutputFor = $state<number | null>(null);

  let jenkinsJobDraft = $state('');
  let jenkinsJobUnlocked = $state(false);
  let jenkinsJobSaving = $state(false);

  let jenkinsPipelines = $state<JenkinsPipelineItem[]>([]);
  let jenkinsPipelinesConfigured = $state(false);
  let jenkinsPipelinesError = $state<string | null>(null);

  /** Background GitLab sync (branches / MRs) after showing DB snapshot */
  let remoteSyncBusy = $state(false);
  /** Live Jenkins `listProjectPipelines` in flight (auto ≤1/h or manual refresh). */
  let jenkinsLiveRefreshing = $state(false);

  /** Supersedes overlapping load() calls so an older request cannot leave loading stuck or flash wrong data. */
  let loadGeneration = 0;

  $effect(() => {
    projectId;
    void load();
  });

  $effect(() => {
    if (route.current.name !== 'project' || route.current.projectId !== projectId) return;
    const v = route.current.view;
    if (v === 'overview' || v === 'branches' || v === 'merge-requests' || (v && v.startsWith('ext-'))) {
      tab = v;
      route.consumeProjectView();
    }
  });

  $effect(() => {
    if (project) {
      workspaceOpen.set({ projectId: project.id, branch: project.defaultBranch ?? null });
    } else {
      workspaceOpen.set(null);
    }
    return () => workspaceOpen.set(null);
  });

  function sleep(ms: number) {
    return new Promise<void>(resolve => setTimeout(resolve, ms));
  }

  function applyJenkinsSnapshotFromProject(p: ProjectDetail, maxRows: number) {
    jenkinsPipelinesConfigured = p.jenkinsPipelinesConfigured ?? false;
    const c = p.jenkinsPipelinesCache ?? null;
    jenkinsPipelines =
      c !== null && c.length > 0 ? c.slice(0, maxRows) : c !== null ? c : [];
    jenkinsPipelinesError = null;
  }

  /** After sync worker finishes, merge fresh DB rows into the page (and Jenkins builds if the job path changed). */
  async function refreshProjectWhenSyncIdle(gen: number, pid: number) {
    let sawRunning = false;
    let showedSpinner = false;
    try {
      for (let i = 0; i < 240; i++) {
        if (i > 0) await sleep(250);
        if (gen !== loadGeneration) return;
        const st = await api.getSyncStatus();
        if (st.state === 'Running') sawRunning = true;
        if (st.state === 'Running' || st.queueDepth > 0) {
          if (!showedSpinner) {
            remoteSyncBusy = true;
            showedSpinner = true;
          }
        }
        if (st.state === 'Idle' && st.queueDepth === 0 && (sawRunning || i >= 1)) break;
        if (st.state === 'Error' && st.queueDepth === 0) break;
      }
      if (gen !== loadGeneration) return;
      const projRes = await api.getProject(pid);
      if (gen !== loadGeneration) return;
      if (projRes?.project) {
        project = projRes.project;
        if (!jenkinsJobUnlocked) jenkinsJobDraft = projRes.project.jenkinsJobPath ?? '';
        applyJenkinsSnapshotFromProject(projRes.project, 5);
      }
      if (shouldAutoFetchJenkinsPipelinesFromLive(pid)) {
        try {
          const plRes = await api.listProjectPipelines(pid, 5);
          if (gen !== loadGeneration) return;
          jenkinsPipelines = plRes.pipelines ?? [];
          jenkinsPipelinesConfigured = plRes.jenkinsConfigured;
          jenkinsPipelinesError = plRes.error ?? null;
          recordJenkinsPipelinesLiveFetch(pid);
        } catch {
          /* Jenkins list is optional */
        }
      }
    } catch {
      /* sync poll / refetch is best-effort */
    } finally {
      if (gen === loadGeneration) remoteSyncBusy = false;
    }
  }

  async function load() {
    const gen = ++loadGeneration;
    loading = true;
    error = null;
    remoteSyncBusy = false;
    try {
      const projRes = await api.getProject(projectId);
      if (gen !== loadGeneration) return;
      project = projRes?.project ?? null;
      jenkinsJobUnlocked = false;
      jenkinsJobDraft = project?.jenkinsJobPath ?? '';
      if (project) {
        applyJenkinsSnapshotFromProject(project, 5);
      } else {
        jenkinsPipelines = [];
        jenkinsPipelinesConfigured = false;
        jenkinsPipelinesError = null;
      }
      loading = false;

      if (project) {
        if (!isProjectBranchMrSyncFresh(project.branchesMrsSyncedAt)) {
          void api.startSync({ scope: 'Project', projectId, reason: 'project-detail-open' }).catch(() => {});
          void refreshProjectWhenSyncIdle(gen, projectId);
        }
      }

      const extRes = await api.listExtensions();
      if (gen !== loadGeneration) return;
      projectExtensions = extRes.extensions.filter(e =>
        e.isEnabled && e.targets.some(t => t.view === 'project')
      );

      if (project) {
        void api.markProjectVisited(project.id).catch(() => {});
      }

      await Promise.all(projectExtensions.map(loadLatestRun));
      if (gen !== loadGeneration) return;

      if (shouldAutoFetchJenkinsPipelinesFromLive(projectId)) {
        jenkinsLiveRefreshing = true;
        try {
          const plRes = await api.listProjectPipelines(projectId, 5);
          if (gen !== loadGeneration) return;
          jenkinsPipelines = plRes.pipelines ?? [];
          jenkinsPipelinesConfigured = plRes.jenkinsConfigured;
          jenkinsPipelinesError = plRes.error ?? null;
          recordJenkinsPipelinesLiveFetch(projectId);
        } catch {
          /* keep snapshot from getProject */
        } finally {
          if (gen === loadGeneration) jenkinsLiveRefreshing = false;
        }
      }
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      if (gen === loadGeneration) loading = false;
    }
  }

  $effect(() => {
    if (!project || loading) return;
    if (!jenkinsJobUnlocked) return;
    void jenkinsJobDraft;
    const draft = jenkinsJobDraft.trim();
    const cur = (project.jenkinsJobPath ?? '').trim();
    if (draft === cur) return;
    const id = setTimeout(async () => {
      if (jenkinsJobDraft.trim() !== draft || !project) return;
      jenkinsJobSaving = true;
      error = null;
      try {
        const r = await api.setProjectJenkinsJob(project.id, draft || null);
        project = r.project;
        jenkinsJobDraft = r.project.jenkinsJobPath ?? '';
      } catch (err) {
        error = err instanceof Error ? err.message : String(err);
      } finally {
        jenkinsJobSaving = false;
      }
    }, 500);
    return () => clearTimeout(id);
  });

  async function loadLatestRun(ext: ExtensionView) {
    try {
      const res = await api.listRuns({ extensionId: ext.id, projectId, take: 5 });
      const defBranch = project?.defaultBranch ?? null;
      const match = res.runs.find(r => defBranch && r.branch === defBranch) ?? res.runs[0] ?? null;
      runsByExt = { ...runsByExt, [ext.id]: match };
    } catch {
      runsByExt = { ...runsByExt, [ext.id]: null };
    }
  }

  async function manualRefreshJenkinsOverview() {
    if (!project) return;
    const gen = loadGeneration;
    jenkinsLiveRefreshing = true;
    try {
      const plRes = await api.listProjectPipelines(project.id, 5);
      if (gen !== loadGeneration) return;
      jenkinsPipelines = plRes.pipelines ?? [];
      jenkinsPipelinesConfigured = plRes.jenkinsConfigured;
      jenkinsPipelinesError = plRes.error ?? null;
      recordJenkinsPipelinesLiveFetch(project.id);
    } catch {
      /* keep list */
    } finally {
      if (gen === loadGeneration) jenkinsLiveRefreshing = false;
    }
  }

  async function runExtension(ext: ExtensionView) {
    if (!project) return;
    runningExtId = ext.id;
    runErrors = { ...runErrors, [ext.id]: null };
    try {
      await api.runExtension(
        {
          extensionId: ext.id,
          projectId: project.id,
          branch: project.defaultBranch ?? null,
          parameters: {},
        },
        extensionRunIpcTimeoutMs(ext.timeoutSeconds),
      );
      await loadLatestRun(ext);
    } catch (err) {
      runErrors = { ...runErrors, [ext.id]: err instanceof Error ? err.message : String(err) };
    } finally {
      runningExtId = null;
    }
  }

  function parseDotnetReview(json: string | null): DotnetReviewStats | null {
    if (!json) return null;
    try {
      const v = JSON.parse(json) as DotnetReviewStats;
      if (typeof v?.schema === 'string' && v.schema.startsWith('dotnet-review/')) return v;
      return null;
    } catch {
      return null;
    }
  }

  function statusTone(status: string): 'passed' | 'running' | 'failed' | 'queued' | 'neutral' {
    switch (status) {
      case 'Succeeded': return 'passed';
      case 'Running': return 'running';
      case 'Failed':
      case 'TimedOut': return 'failed';
      case 'Cancelled': return 'queued';
      default: return 'neutral';
    }
  }

  function extTabId(id: number) { return `ext-${id}`; }

  function toggleJenkinsJobLock() {
    if (jenkinsJobUnlocked) {
      if (project) {
        jenkinsJobDraft = project.jenkinsJobPath ?? '';
      }
      jenkinsJobUnlocked = false;
    } else {
      jenkinsJobUnlocked = true;
    }
  }

  function tabLabel(ext: ExtensionView): string {
    const t = ext.targets.find(x => x.view === 'project');
    return (t?.label && t.label.trim() !== '') ? t.label : ext.name;
  }

  const parts = $derived(project ? splitPath(project.pathWithNamespace) : { ns: '', name: '' });
  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';

  const tabs = $derived([
    { id: 'overview' as string, label: 'Overview' },
    { id: 'branches' as string, label: 'Branches' },
    { id: 'merge-requests' as string, label: 'Merge requests' },
    { id: 'pipelines' as string, label: 'Pipelines' },
    ...projectExtensions.map(e => ({ id: extTabId(e.id), label: tabLabel(e) })),
  ]);

  function onProjectTabSelect(id: string) {
    if (!project) return;
    if (id === 'pipelines') {
      route.goProjectPipelines(project.id);
      return;
    }
    tab = id;
  }

  const activeExt = $derived(
    tab.startsWith('ext-')
      ? projectExtensions.find(e => extTabId(e.id) === tab) ?? null
      : null
  );

  const statCells = $derived(project ? [
    { label: 'Default', value: project.defaultBranch ?? '—', mono: true, accent: false },
    { label: 'Branches', value: String(project.branches.length), mono: false, accent: false },
    { label: 'Open MRs', value: String(project.openMergeRequests.length), mono: false, accent: project.openMergeRequests.length > 0 },
    { label: 'Drafts', value: String(project.openMergeRequests.filter(m => m.draft).length), mono: false, accent: false },
    { label: 'Archived', value: project.archived ? 'yes' : 'no', mono: false, accent: false },
    { label: 'Activity', value: timeAgo(project.lastActivityAt), mono: false, accent: false },
  ] : []);

  const chrome = $derived(theme.chrome);
  const kpiNumWeight = $derived(kpiNumeralFontWeightCss(chrome));
  const pipelinesSectionTitle = $derived(
    chrome.sectionLabelCasing === 'sentence' ? 'Pipelines' : 'PIPELINES',
  );
</script>

<div class="flex min-h-0 flex-1 flex-col">
  {#if loading}
    <p class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
  {:else if error}
    <p class="p-6 text-danger">{error}</p>
  {:else if !project}
    <p class="p-6 text-mute">Project not found.</p>
  {:else}
    <PageHeader
      breadcrumbSyncing={remoteSyncBusy || jenkinsLiveRefreshing}
      crumbs={[
        { segment: 'projects', onNavigate: () => route.goProjects() },
        ...parts.ns.replace(/\/$/, '').split('/').filter(Boolean),
        parts.name,
      ]}
      title={parts.name}
    />

    <ProjectViewTabs {tabs} activeId={tab} onSelect={onProjectTabSelect} {chrome} />

    <div class="grid grid-cols-6 gap-[1px] bg-hair">
      {#each statCells as c (c.label)}
        <div class="flex flex-col gap-1 bg-paper px-4 py-3">
          <div style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? c.label : c.label.toUpperCase()}</div>
          <div
            class:text-accent={c.accent}
            class:text-ink={!c.accent}
            style="font-family:{c.mono || !chrome.pageHeaderSerifTitle ? 'var(--font-mono)' : 'var(--font-serif)'}; font-size:{chrome.pageHeaderSerifTitle ? '20px' : '18px'}; font-weight:{kpiNumWeight}; letter-spacing:-.01em"
          >
            {c.value}
          </div>
        </div>
      {/each}
    </div>

    <div class="min-h-0 flex-1 overflow-auto p-6 bg-bg">
      {#if tab === 'overview'}
        <div class="grid grid-cols-[2fr_1fr] gap-6">
          <div class="space-y-4">
            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3 flex items-center justify-between">
                <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Jenkins job' : 'JENKINS JOB'}</h2>
                {#if jenkinsJobSaving}
                  <span class="text-mute" style="font-family:var(--font-mono); font-size:10.5px">saving…</span>
                {/if}
              </div>
              <p class="mb-3 text-mute" style="font-size:12px; line-height:1.5">
                When Jenkins base URL is set, sync reads this project’s GitLab webhooks and fills the job path if a hook
                URL matches that Jenkins (Multibranch <span style="font-family:var(--font-mono)">project/…</span> or
                <span style="font-family:var(--font-mono)">job/…/job/…</span>). You can still override here — a leading
                <span style="font-family:var(--font-mono)">project/</span> is stripped so API calls use
                <span style="font-family:var(--font-mono)">/job/a/job/b</span>. Unlock the field to edit; while locked, changes are not saved.
              </p>
              <div class="relative min-w-0 w-full">
                <input
                  type="text"
                  bind:value={jenkinsJobDraft}
                  readonly={!jenkinsJobUnlocked}
                  placeholder="project/melody-master/number-porting-in-worker_master"
                  class="focus-ring w-full min-w-0 border border-hair-strong bg-bg py-2 pl-3 pr-10 text-ink placeholder:text-mute"
                  class:text-mute={!jenkinsJobUnlocked}
                  class:cursor-text={jenkinsJobUnlocked}
                  class:cursor-default={!jenkinsJobUnlocked}
                  style="font-family:var(--font-mono); font-size:12px; border-radius:var(--radius)"
                />
                <button
                  type="button"
                  onclick={toggleJenkinsJobLock}
                  class="focus-ring absolute right-1 top-1/2 -translate-y-1/2 rounded px-2 py-1 text-[14px] leading-none text-mute transition-colors hover:bg-panel-2 hover:text-ink"
                  title={jenkinsJobUnlocked ? 'Lock (discard unsaved edits)' : 'Unlock to edit'}
                  aria-label={jenkinsJobUnlocked ? 'Lock Jenkins job path' : 'Unlock Jenkins job path to edit'}
                >
                  {jenkinsJobUnlocked ? '🔓' : '🔒'}
                </button>
              </div>
            </section>

            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3 flex items-center justify-between gap-2">
                <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Open merge requests' : 'OPEN MERGE REQUESTS'}</h2>
                <button
                  type="button"
                  class="shrink-0 text-accent hover:underline"
                  style="font-family:var(--font-mono); font-size:10.5px"
                  onclick={() => project && route.goProject(project.id, 'merge-requests')}
                >
                  View all →
                </button>
              </div>
              {#if project.openMergeRequests.length === 0}
                <p class="text-mute" style="font-size:12px">No open merge requests.</p>
              {:else}
                {@const pid = project.id}
                <ul class="divide-y divide-hair">
                  {#each project.openMergeRequests as mr (mr.id)}
                    <li>
                      <button
                        type="button"
                        onclick={() => route.goMr(pid, mr.iid)}
                        class="flex w-full items-start gap-3 py-3 text-left hover:bg-panel-2"
                      >
                        <span class="shrink-0 text-mute" style="font-family:var(--font-mono); font-size:11.5px">
                          !{mr.iid}
                        </span>
                        <div class="min-w-0 flex-1">
                          <div class="truncate text-ink" style="font-size:13px">{mr.title}</div>
                          <div class="mt-0.5 text-mute" style="font-family:var(--font-mono); font-size:10.5px">
                            {mr.authorUsername ?? '—'} · {mr.sourceBranch} → {mr.targetBranch} · {timeAgo(mr.updatedAt)}
                          </div>
                        </div>
                        {#if mr.draft}
                          <Pill tone="draft">draft</Pill>
                        {:else}
                          <Pill tone="open">open</Pill>
                        {/if}
                      </button>
                    </li>
                  {/each}
                </ul>
              {/if}
            </section>

            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Activity' : 'ACTIVITY'}</h2></div>
              <p class="text-mute" style="font-size:12px">
                Activity feed is not cached locally yet.
              </p>
            </section>
          </div>

          <div class="space-y-4">
            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3 flex items-center justify-between gap-2">
                <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Branches' : 'BRANCHES'}</h2>
                <button
                  type="button"
                  class="shrink-0 text-accent hover:underline"
                  style="font-family:var(--font-mono); font-size:10.5px"
                  onclick={() => project && route.goProject(project.id, 'branches')}
                >
                  View all →
                </button>
              </div>
              {#if project.branches.length === 0}
                <p class="text-mute" style="font-size:12px">No branches synced.</p>
              {:else}
                <ul class="divide-y divide-hair">
                  {#each project.branches.slice(0, 12) as b (b.id)}
                    <li class="flex items-center gap-2 py-2">
                      {#if b.isProtected}<span class="text-gold" title="protected">🔒</span>{/if}
                      <span
                        class="min-w-0 flex-1 truncate"
                        class:text-ink={b.isDefault}
                        class:text-subink={!b.isDefault}
                        style="font-family:var(--font-mono); font-size:11.5px"
                      >
                        {b.name}
                      </span>
                      {#if b.isDefault}<Pill tone="open">default</Pill>{/if}
                    </li>
                  {/each}
                </ul>
              {/if}
            </section>

            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3 flex items-center justify-between gap-2">
                <h2 style={labelFont}>{pipelinesSectionTitle}</h2>
                <div class="flex shrink-0 items-center gap-3">
                  <button
                    type="button"
                    class="rounded border border-hair bg-paper px-2 py-1 text-[11px] text-ink transition-colors hover:bg-panel-2 disabled:opacity-50"
                    style="font-family:var(--font-mono); border-width:{chrome.warmTitleBar ? '0.5px' : '1px'}"
                    disabled={jenkinsLiveRefreshing}
                    onclick={manualRefreshJenkinsOverview}
                  >
                    Refresh
                  </button>
                  <button
                    type="button"
                    class="shrink-0 text-accent hover:underline"
                    style="font-family:var(--font-mono); font-size:10.5px"
                    onclick={() => project && route.goProjectPipelines(project.id)}
                  >
                    View all →
                  </button>
                </div>
              </div>
              <JenkinsPipelineList
                condensed
                includeHeading={false}
                pipelines={jenkinsPipelines}
                jenkinsConfigured={jenkinsPipelinesConfigured}
                error={jenkinsPipelinesError}
              />
            </section>

            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'README' : 'README'}</h2></div>
              <p class="text-mute" style="font-size:12px">
                {project.description ?? 'No description synced.'}
              </p>
            </section>
          </div>
        </div>
      {:else if tab === 'branches'}
        <div class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
          <table class="w-full border-collapse">
            <thead>
              <tr class="border-b border-hair text-left" style={labelFont}>
                <th class="py-2 pl-5">{chrome.sectionLabelCasing === 'sentence' ? 'Name' : 'NAME'}</th>
                <th class="py-2">{chrome.sectionLabelCasing === 'sentence' ? 'Commit' : 'COMMIT'}</th>
                <th class="py-2">{chrome.sectionLabelCasing === 'sentence' ? 'Flags' : 'FLAGS'}</th>
                <th class="py-2 pr-5 text-right">{chrome.sectionLabelCasing === 'sentence' ? 'Updated' : 'UPDATED'}</th>
              </tr>
            </thead>
            <tbody>
              {#each project.branches as b (b.id)}
                <tr class="border-b border-hair">
                  <td class="py-2 pl-5 text-ink" style="font-family:var(--font-mono); font-size:12px">{b.name}</td>
                  <td class="py-2 text-subink" style="font-family:var(--font-mono); font-size:11px">{b.sha.slice(0, 8)}</td>
                  <td class="py-2" style="font-family:var(--font-mono); font-size:10.5px">
                    {#if b.isDefault}<Pill tone="open">default</Pill>{/if}
                    {#if b.isProtected}<Pill tone="queued">protected</Pill>{/if}
                  </td>
                  <td class="py-2 pr-5 text-right text-mute" style="font-family:var(--font-mono); font-size:11px">
                    {timeAgo(b.updatedAt)}
                  </td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {:else if tab === 'merge-requests'}
        <div class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
          {#if project.openMergeRequests.length === 0}
            <p class="p-5 text-mute" style="font-size:12px">No open merge requests.</p>
          {:else}
            {@const pid2 = project.id}
            <ul class="divide-y divide-hair">
              {#each project.openMergeRequests as mr (mr.id)}
                <li>
                  <button
                    type="button"
                    onclick={() => route.goMr(pid2, mr.iid)}
                    class="flex w-full items-start gap-3 px-5 py-3 text-left hover:bg-panel-2"
                  >
                    <span class="shrink-0 text-mute" style="font-family:var(--font-mono); font-size:11.5px">!{mr.iid}</span>
                    <div class="min-w-0 flex-1">
                      <div class="text-ink" style="font-size:13px">{mr.title}</div>
                      <div class="mt-1 text-mute" style="font-family:var(--font-mono); font-size:10.5px">
                        {mr.authorUsername ?? '—'} · {mr.sourceBranch} → {mr.targetBranch} · {timeAgo(mr.updatedAt)}
                      </div>
                    </div>
                    {#if mr.draft}<Pill tone="draft">draft</Pill>{:else}<Pill tone="open">open</Pill>{/if}
                  </button>
                </li>
              {/each}
            </ul>
          {/if}
        </div>
      {:else if activeExt}
        {@const ext = activeExt}
        {@const run = runsByExt[ext.id] ?? null}
        {@const dotnetStats = ext.name === 'dotnet-review' ? parseDotnetReview(run?.statsJson ?? null) : null}
        {@const running = runningExtId === ext.id}
        {@const runError = runErrors[ext.id] ?? null}
        {@const branch = project.defaultBranch ?? '—'}
        <div class="space-y-4">
          <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? ext.name : ext.name.toUpperCase()}</h2>
                {#if ext.description}
                  <p class="mt-1 text-subink" style="font-size:12.5px">{ext.description}</p>
                {/if}
                <p class="mt-1 text-subink" style="font-size:12.5px">
                  Target branch: <span class="text-ink" style="font-family:var(--font-mono); font-size:12px">{branch}</span>.
                  {#if run}
                    <span class="text-mute"> — last run {timeAgo(run.startedAt)}</span>
                  {/if}
                </p>
                {#if run}
                  <div class="mt-2 flex items-center gap-2">
                    <Pill tone={statusTone(run.status)}>
                      {run.status}{run.exitCode !== null ? ` (${run.exitCode})` : ''}
                    </Pill>
                    {#if run.branch !== branch}
                      <span class="text-mute" style="font-family:var(--font-mono); font-size:11px">
                        on {run.branch}
                      </span>
                    {/if}
                  </div>
                {/if}
              </div>
              <div class="flex items-center gap-2">
                <button
                  type="button"
                  onclick={() => runExtension(ext)}
                  disabled={running || !project.defaultBranch}
                  class="border border-accent px-3 py-1.5 text-[12px] disabled:opacity-50"
                  style="border-radius:var(--radius); color:var(--paper); background:var(--accent)"
                >
                  {running ? 'Running…' : run ? 'Re-run' : 'Run'}
                </button>
              </div>
            </div>
            {#if runError}
              <p class="mt-3 text-danger" style="font-size:12.5px">{runError}</p>
            {/if}
            {#if run?.errorMessage}
              <pre class="mt-3 overflow-x-auto border border-danger p-2 text-danger"
                   style="border-radius:var(--radius); background:var(--danger-soft); font-family:var(--font-mono); font-size:11.5px">{run.errorMessage}</pre>
            {/if}
          </section>

          {#if dotnetStats}
            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <DotnetReviewResult
                stats={dotnetStats}
                hiddenCodes={ext.settingsValues?.hiddenCodes ?? ''}
                workProjectId={project.id}
                workBranch={project.defaultBranch}
              />
            </section>
          {:else if run?.statsJson}
            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <p class="text-mute" style="font-size:12px">Run output:</p>
              <pre class="mt-2 overflow-x-auto bg-panel-2 p-2 text-subink"
                   style="border-radius:var(--radius); font-family:var(--font-mono); font-size:11px">{run.statsJson}</pre>
            </section>
          {:else if !running && !run}
            <section class="border border-hair bg-paper p-8 text-center" style="border-radius:var(--radius-lg)">
              <p class="text-mute" style="font-family:var(--font-mono); font-size:11.5px; letter-spacing:.08em">
                NO RUN YET
              </p>
              <p class="mt-2 text-subink" style="font-size:12px">
                Run {ext.name} to produce a report for this project.
              </p>
            </section>
          {/if}

          {#if run}
            {@const runId = run.id}
            <div>
              <button
                type="button"
                onclick={() => (showOutputFor = showOutputFor === ext.id ? null : ext.id)}
                class="text-mute hover:text-ink"
                style="font-family:var(--font-mono); font-size:11px"
              >
                {showOutputFor === ext.id ? 'hide output ▲' : 'show output ▼'}
              </button>
            </div>
            {#if showOutputFor === ext.id}
              <ExtensionRunLogViewer runId={runId} autoLoad />
            {/if}
          {/if}
        </div>
      {/if}
    </div>
  {/if}
</div>
