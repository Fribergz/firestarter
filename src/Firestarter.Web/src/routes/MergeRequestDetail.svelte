<script lang="ts">
  import {
    api,
    extensionRunIpcTimeoutMs,
    type GitlabConfig,
    type JenkinsBuildStatus,
    type MergeRequestDto,
    type ProjectDetail,
    type ExtensionRunSummary,
    type ExtensionView,
    type MergeRequestOverview,
    type MergeRequestCommit,
    type MergeRequestFileChange,
    type MergeRequestDiscussion,
  } from '../lib/api';
  import MrOverview from '../lib/components/MrOverview.svelte';
  import MrCommits from '../lib/components/MrCommits.svelte';
  import MrChanges from '../lib/components/MrChanges.svelte';
  import MrDiscussion from '../lib/components/MrDiscussion.svelte';
  import { route } from '../lib/stores/route.svelte';
  import { workspaceOpen } from '../lib/stores/workspaceOpenContext.svelte';
  import { approvedMrs } from '../lib/stores/approved.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import {
    breadcrumbRowInlineStyle,
    formatBreadcrumbSegment,
    primaryHeadingStyle,
  } from '../lib/theme/chrome';
  import Pill from '../lib/components/Pill.svelte';
  import DotnetReviewResult, { type DotnetReviewStats } from '../lib/components/DotnetReviewResult.svelte';
  import ExtensionRunLogViewer from '../lib/components/ExtensionRunLogViewer.svelte';
  import { timeAgo, splitPath } from '../lib/format';

  interface Props {
    projectId: number;
    mrId: number;
    /** Breadcrumb parent after Start: project list vs reviewing queue. */
    listParent: 'project' | 'reviewing';
  }
  let { projectId, mrId, listParent }: Props = $props();

  let mr = $state<MergeRequestDto | null>(null);
  let project = $state<ProjectDetail | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);

  type FixedTab = 'overview' | 'commits' | 'changes' | 'discussion';
  let tab = $state<string>('overview');

  let mrExtensions = $state<ExtensionView[]>([]);
  let runsByExt = $state<Record<number, ExtensionRunSummary | null>>({});
  let runningExtId = $state<number | null>(null);
  let runErrors = $state<Record<number, string | null>>({});
  let showOutputFor = $state<number | null>(null);

  let jenkins = $state<JenkinsBuildStatus | null>(null);
  let jenkinsLoading = $state(false);
  let jenkinsTriggering = $state(false);
  let jenkinsActionError = $state<string | null>(null);

  let approving = $state(false);
  let approveError = $state<string | null>(null);

  /** GitLab login from settings — used to gate the Merge action to assignees only. */
  let currentUsername = $state<string | null>(null);

  // Per-tab data is fetched lazily on first visit and cached for the rest of the session.
  let overview = $state<MergeRequestOverview | null>(null);
  let commits = $state<MergeRequestCommit[] | null>(null);
  let changes = $state<MergeRequestFileChange[] | null>(null);
  let discussions = $state<MergeRequestDiscussion[] | null>(null);
  let tabLoading = $state<Record<FixedTab, boolean>>({ overview: false, commits: false, changes: false, discussion: false });
  let tabError = $state<Record<FixedTab, string | null>>({ overview: null, commits: null, changes: null, discussion: null });

  let loadGeneration = 0;

  $effect(() => {
    projectId;
    mrId;
    void load();
  });

  $effect(() => {
    if (project && mr) {
      workspaceOpen.set({ projectId: project.id, branch: mr.sourceBranch });
    } else {
      workspaceOpen.set(null);
    }
    return () => workspaceOpen.set(null);
  });

  async function load() {
    const gen = ++loadGeneration;
    loading = true;
    error = null;
    // Reset per-tab caches on (re)load — avoids leaking another MR's data through.
    overview = null;
    commits = null;
    changes = null;
    discussions = null;
    tabLoading = { overview: false, commits: false, changes: false, discussion: false };
    tabError = { overview: null, commits: null, changes: null, discussion: null };
    try {
      const [projRes, mrRes, settingsRes] = await Promise.all([
        api.getProject(projectId),
        api.getMergeRequest(projectId, mrId),
        api.getSettings().catch((): GitlabConfig | null => null),
      ]);
      if (gen !== loadGeneration) return;
      project = projRes?.project ?? null;
      mr = mrRes?.mr ?? null;
      currentUsername = settingsRes?.currentUsername ?? null;
      loading = false;

      if (!mr) return;
      // Kick off the initial tab's fetch right away so the user doesn't see an empty page.
      void loadTab(tab as FixedTab, gen);

      const extRes = await api.listExtensions();
      if (gen !== loadGeneration) return;
      mrExtensions = extRes.extensions.filter(e =>
        e.isEnabled && e.targets.some(t => t.view === 'merge-request')
      );
      await Promise.all(mrExtensions.map(loadLatestRun));
      void loadJenkinsStatus();
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      if (gen === loadGeneration) loading = false;
    }
  }

  /** Fetch the data for `t` if it hasn't been fetched yet. Cached values stay in state for the session. */
  async function loadTab(t: FixedTab, gen: number = loadGeneration) {
    if (!mr) return;
    if (t !== 'overview' && t !== 'commits' && t !== 'changes' && t !== 'discussion') return;
    // Overview: if the snapshot is already in memory, only prefetch discussions if still missing.
    if (t === 'overview' && overview !== null) {
      if (discussions === null && !tabLoading.discussion) void loadTab('discussion', gen);
      return;
    }
    if (
      (t === 'commits' && commits !== null) ||
      (t === 'changes' && changes !== null) ||
      (t === 'discussion' && discussions !== null)
    ) return;
    if (tabLoading[t]) return;

    /** On first visit to overview, also lazy-load discussions in parallel (Discussion tab reuses the cache). */
    const alsoDisc = t === 'overview' && discussions === null;
    tabLoading = { ...tabLoading, [t]: true, ...(alsoDisc ? { discussion: true } : {}) };
    tabError = { ...tabError, [t]: null, ...(alsoDisc ? { discussion: null } : {}) };
    try {
      if (t === 'overview') {
        if (alsoDisc) {
          const [r, dr] = await Promise.all([
            api.getMrOverview(projectId, mr.iid),
            api.getMrDiscussions(projectId, mr.iid),
          ]);
          if (gen !== loadGeneration) return;
          overview = r?.overview ?? null;
          discussions = dr.discussions;
        } else {
          const r = await api.getMrOverview(projectId, mr.iid);
          if (gen !== loadGeneration) return;
          overview = r?.overview ?? null;
        }
      } else if (t === 'commits') {
        const r = await api.getMrCommits(projectId, mr.iid);
        if (gen !== loadGeneration) return;
        commits = r.commits;
      } else if (t === 'changes') {
        const r = await api.getMrChanges(projectId, mr.iid);
        if (gen !== loadGeneration) return;
        changes = r.changes;
      } else if (t === 'discussion') {
        const r = await api.getMrDiscussions(projectId, mr.iid);
        if (gen !== loadGeneration) return;
        discussions = r.discussions;
      }
    } catch (err) {
      if (gen !== loadGeneration) return;
      const msg = err instanceof Error ? err.message : String(err);
      tabError = { ...tabError, [t]: msg, ...(alsoDisc ? { discussion: msg } : {}) };
    } finally {
      if (gen === loadGeneration) {
        const next = { ...tabLoading, [t]: false };
        if (alsoDisc) next.discussion = false;
        tabLoading = next;
      }
    }
  }

  function selectTab(id: string) {
    tab = id;
    if (id === 'overview' || id === 'commits' || id === 'changes' || id === 'discussion') {
      void loadTab(id);
    }
  }

  async function loadJenkinsStatus() {
    if (!project) {
      jenkins = null;
      return;
    }
    jenkinsLoading = true;
    jenkinsActionError = null;
    try {
      jenkins = await api.getJenkinsBuildStatus(project.id);
    } catch {
      jenkins = null;
    } finally {
      jenkinsLoading = false;
    }
  }

  async function approve() {
    if (!mr || approving) return;
    approving = true;
    approveError = null;
    try {
      await api.approveMr(projectId, mr.iid);
      // Mark before navigating so the reviewer list hides this row immediately, before the per-row
      // GitLab enrichment confirms `approvedByMe` for itself.
      approvedMrs.mark(projectId, mr.iid);
      route.goMrReviewer();
    } catch (err) {
      approveError = err instanceof Error ? err.message : String(err);
      approving = false;
    }
  }

  async function triggerJenkins() {
    if (!project || !mr) return;
    jenkinsTriggering = true;
    jenkinsActionError = null;
    try {
      await api.triggerJenkinsBuild(project.id, { branch: mr.sourceBranch, mrIid: mr.iid });
      await loadJenkinsStatus();
    } catch (err) {
      jenkinsActionError = err instanceof Error ? err.message : String(err);
    } finally {
      jenkinsTriggering = false;
    }
  }

  async function loadLatestRun(ext: ExtensionView) {
    try {
      const res = await api.listRuns({ extensionId: ext.id, projectId, take: 5 });
      const match = res.runs.find(r => mr && r.branch === mr.sourceBranch) ?? res.runs[0] ?? null;
      runsByExt = { ...runsByExt, [ext.id]: match };
    } catch {
      runsByExt = { ...runsByExt, [ext.id]: null };
    }
  }

  async function runExtension(ext: ExtensionView) {
    if (!mr) return;
    runningExtId = ext.id;
    runErrors = { ...runErrors, [ext.id]: null };
    try {
      await api.runExtension(
        {
          extensionId: ext.id,
          projectId,
          branch: mr.sourceBranch,
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

  function tabLabel(ext: ExtensionView): string {
    const t = ext.targets.find(x => x.view === 'merge-request');
    return (t?.label && t.label.trim() !== '') ? t.label : ext.name;
  }

  const parts = $derived(project ? splitPath(project.pathWithNamespace) : { ns: '', name: '' });
  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';

  const tabs = $derived([
    { id: 'overview' as string, label: 'Overview' },
    { id: 'commits' as string, label: 'Commits' },
    { id: 'changes' as string, label: 'Changes' },
    { id: 'discussion' as string, label: 'Discussion' },
    ...mrExtensions.map(e => ({ id: extTabId(e.id), label: tabLabel(e) })),
  ]);

  const activeExt = $derived(
    tab.startsWith('ext-')
      ? mrExtensions.find(e => extTabId(e.id) === tab) ?? null
      : null
  );

  const chrome = $derived(theme.chrome);
  const crumbStyle = $derived(breadcrumbRowInlineStyle(chrome));

  function fmtCrumb(s: string) {
    return formatBreadcrumbSegment(chrome, s);
  }

  const nsParts = $derived(parts.ns.replace(/\/$/, '').split('/').filter(Boolean));

  const titleStyle = $derived(primaryHeadingStyle(chrome, 'detail'));

  const branchHintColor = $derived(chrome.detailMetaUsesInfoTint ? 'var(--info)' : 'var(--mute)');

  function isCurrentUserAssignee(assigneeUsernames: string | null, username: string | null): boolean {
    if (!username?.trim() || !assigneeUsernames?.trim()) return false;
    const u = username.trim().toLowerCase();
    return assigneeUsernames
      .split(',')
      .map(s => s.trim().toLowerCase())
      .filter(s => s.length > 0)
      .includes(u);
  }

  const canShowMerge = $derived(
    mr != null && isCurrentUserAssignee(mr.assigneeUsernames, currentUsername),
  );

  /** Compact actions row; padding slightly above meta line height without chip-scale bulk. */
  const mrActionBtnStyle =
    'font-family:var(--font-sans); font-size:11px; line-height:15px; padding:4px 12px; box-sizing:border-box; border-radius:var(--radius)';
</script>

<div class="flex min-h-0 flex-1 flex-col">
  {#if loading}
    <p class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
  {:else if error}
    <p class="p-6 text-danger">{error}</p>
  {:else if !mr}
    <p class="p-6 text-mute">Merge request not found.</p>
  {:else}
    <header class="border-b border-hair bg-paper px-6 pt-4">
      <div class="min-w-0">
        <!-- Crumb row (scrolls). Scrollbar hidden so the track doesn’t add height under the line. -->
        <div class="flex items-center gap-3">
        <div
          class="min-w-0 flex-1 overflow-x-auto overflow-y-hidden [-ms-overflow-style:none] [scrollbar-width:none] [-webkit-overflow-scrolling:touch] [&::-webkit-scrollbar]:hidden"
        >
          <div
            class="flex w-max max-w-none flex-nowrap items-center gap-x-1 leading-none text-subink"
            style={crumbStyle}
          >
            {#if project}
              {@const pid = project.id}
              <button
                type="button"
                onclick={() => route.goStart()}
                class="inline-flex shrink-0 rounded p-0.5 text-subink transition-colors hover:bg-panel-2 hover:text-ink"
                aria-label="Start"
                title="Start"
              >
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  width="14"
                  height="14"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
                  <polyline points="9 22 9 12 15 12 15 22" />
                </svg>
              </button>
              <span class="text-mute shrink-0 select-none" aria-hidden="true">›</span>
              {#if listParent === 'reviewing'}
                <button type="button" class="shrink-0 hover:text-ink" onclick={() => route.goMrReviewer()}>
                  {fmtCrumb('reviewing')}
                </button>
              {:else}
                <button type="button" class="shrink-0 hover:text-ink" onclick={() => route.goProjects()}>
                  {fmtCrumb('projects')}
                </button>
              {/if}
              {#each nsParts as seg (seg)}
                <span class="text-mute shrink-0 select-none" aria-hidden="true">›</span>
                <span class="shrink-0">{fmtCrumb(seg)}</span>
              {/each}
              <span class="text-mute shrink-0 select-none" aria-hidden="true">›</span>
              <button type="button" class="shrink-0 hover:text-ink" onclick={() => route.goProject(pid)}>
                {fmtCrumb(parts.name)}
              </button>
            {/if}
          </div>
        </div>
        </div>

        <!-- Title: id + pill share one centered cluster; h1 aligns on the same row (items-center). -->
        <div class="mt-2 flex items-center gap-3">
          <div class="flex shrink-0 items-center gap-2">
            <span
              class="inline-flex items-center leading-none"
              style="font-family:var(--font-mono); font-size:13px; line-height:1; color:{branchHintColor}"
            >
              !{mr.iid}
            </span>
            {#if mr.draft}
              <Pill tone="draft">draft</Pill>
            {:else}
              <Pill tone="open">open</Pill>
            {/if}
          </div>
          <h1 class="min-w-0 flex-1 text-ink" style={titleStyle}>{mr.title}</h1>
        </div>
      </div>

      <!-- Last row before header border: meta left, actions right; pb hugs the hairline. -->
      <div class="mt-2 flex flex-wrap items-center justify-between gap-x-4 gap-y-2 pb-2">
        <div class="min-w-0 text-mute" style="font-family:var(--font-mono); font-size:11px; letter-spacing:.04em">
          {mr.authorUsername ?? '—'}
          <span class="mx-2">→</span>
          <span class="text-subink">{mr.sourceBranch}</span>
          <span class="mx-2">into</span>
          <span class="text-subink">{mr.targetBranch}</span>
          <span class="mx-2">·</span>
          opened {timeAgo(mr.createdAt)}
        </div>
        <div class="ml-auto flex shrink-0 flex-nowrap items-center gap-2">
          <button
            type="button"
            class="inline-flex items-center justify-center bg-paper text-ink transition-colors hover:bg-panel-2"
            style="{mrActionBtnStyle}; border:0.5px solid var(--hair)"
          >
            Mark draft
          </button>
          <button
            type="button"
            onclick={approve}
            disabled={approving}
            title={approveError ?? 'Approve and return to the reviewing list'}
            class="inline-flex items-center justify-center bg-ok-soft transition-opacity hover:opacity-90 disabled:opacity-50"
            style="{mrActionBtnStyle}; border:0.5px solid var(--ok); color:var(--ok)"
          >
            {approving ? 'Approving…' : 'Approve'}
          </button>
          {#if canShowMerge}
            <button
              type="button"
              class="inline-flex items-center justify-center transition-opacity hover:opacity-90"
              style="{mrActionBtnStyle}; border:0.5px solid var(--accent); color:var(--paper); background:var(--accent)"
            >
              Merge
            </button>
          {/if}
        </div>
      </div>
    </header>

    <div class="flex items-center gap-5 border-b border-hair bg-paper px-6">
      {#each tabs as t (t.id)}
        {@const active = t.id === tab}
        <button
          type="button"
          onclick={() => selectTab(t.id)}
          class="group relative py-3 text-[12px] transition-colors hover:text-ink"
          class:text-ink={active}
          class:text-subink={!active}
          style={active ? 'font-weight:500' : ''}
        >
          {chrome.sectionLabelCasing === 'sentence' ? t.label : t.label.toUpperCase()}
          {#if active}
            <span class="absolute inset-x-0 bottom-0 h-[2px] bg-accent"></span>
          {:else}
            <span class="absolute inset-x-0 bottom-0 h-[2px] bg-hair-strong opacity-0 transition-opacity group-hover:opacity-100"></span>
          {/if}
        </button>
      {/each}
    </div>

    <div class="min-h-0 flex-1 overflow-auto bg-bg p-6">
      {#if tab === 'overview'}
        {#if tabLoading.overview && !overview}
          <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">loading overview…</p>
        {:else if tabError.overview}
          <p class="text-danger" style="font-size:12.5px">{tabError.overview}</p>
        {:else if overview}
          <MrOverview {overview} />
        {:else}
          <p class="text-mute" style="font-size:12px">No overview available.</p>
        {/if}

        <section class="mt-4 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
                <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Jenkins' : 'JENKINS'}</h2>
                {#if jenkins?.configured && jenkins.jobUrl}
                  <a
                    href={jenkins.jobUrl}
                    target="_blank"
                    rel="noreferrer"
                    class="text-accent"
                    style="font-family:var(--font-mono); font-size:11px"
                  >job ↗</a>
                {/if}
              </div>
              {#if jenkinsLoading}
                <p class="text-mute" style="font-size:12px">Loading…</p>
              {:else if !jenkins || !jenkins.configured}
                <p class="text-mute" style="font-size:12px; line-height:1.5">
                  Add your Jenkins base URL and API token under Settings, then set the job path on this project’s Overview
                  tab. GitLab webhooks should target Jenkins separately; this panel uses the Jenkins API.
                </p>
              {:else}
                {#if jenkins.error}
                  <p class="text-danger" style="font-size:12px">{jenkins.error}</p>
                {:else if jenkins.lastBuild}
                  {@const b = jenkins.lastBuild}
                  <div class="flex flex-wrap items-center gap-3 text-subink" style="font-size:12px">
                    <span style="font-family:var(--font-mono)">#{b.number}</span>
                    {#if b.building}
                      <Pill tone="running">running</Pill>
                    {:else if b.result === 'SUCCESS'}
                      <Pill tone="passed">{b.result}</Pill>
                    {:else if b.result}
                      <Pill tone="failed">{b.result}</Pill>
                    {:else}
                      <Pill tone="neutral">finished</Pill>
                    {/if}
                    <a href={b.url} target="_blank" rel="noreferrer" class="text-accent" style="font-family:var(--font-mono); font-size:11px">build ↗</a>
                  </div>
                {:else}
                  <p class="text-mute" style="font-size:12px">No last build reported for this job.</p>
                {/if}
                <div class="mt-3 flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    onclick={triggerJenkins}
                    disabled={jenkinsTriggering}
                    class="border border-hair-strong bg-paper px-3 py-1.5 text-[12px] text-ink hover:bg-panel-2 disabled:opacity-50"
                    style="border-radius:var(--radius)"
                  >
                    {jenkinsTriggering ? 'Queueing…' : 'Trigger build'}
                  </button>
                  <span class="text-mute" style="font-size:11px">
                    Passes <span style="font-family:var(--font-mono)">BRANCH</span> and
                    <span style="font-family:var(--font-mono)">MR_IID</span> if the job has parameters.
                  </span>
                </div>
                {#if jenkinsActionError}
                  <p class="mt-2 text-danger" style="font-size:11.5px">{jenkinsActionError}</p>
                {/if}
              {/if}
        </section>

        <section class="mt-4 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
          <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Links' : 'LINKS'}</h2></div>
          <a href={mr.webUrl} target="_blank" rel="noreferrer" class="text-accent" style="font-family:var(--font-mono); font-size:11.5px">
            open on GitLab ↗
          </a>
        </section>
      {:else if tab === 'commits'}
        {#if tabLoading.commits && commits === null}
          <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">loading commits…</p>
        {:else if tabError.commits}
          <p class="text-danger" style="font-size:12.5px">{tabError.commits}</p>
        {:else if commits}
          <MrCommits {commits} />
        {/if}
      {:else if tab === 'changes'}
        {#if tabLoading.changes && changes === null}
          <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">loading changes…</p>
        {:else if tabError.changes}
          <p class="text-danger" style="font-size:12.5px">{tabError.changes}</p>
        {:else if changes}
          <MrChanges {changes} />
        {/if}
      {:else if tab === 'discussion'}
        {#if tabLoading.discussion && discussions === null}
          <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">loading discussion…</p>
        {:else if tabError.discussion}
          <p class="text-danger" style="font-size:12.5px">{tabError.discussion}</p>
        {:else if discussions}
          <MrDiscussion {discussions} />
        {/if}
      {:else if activeExt}
        {@const ext = activeExt}
        {@const run = runsByExt[ext.id] ?? null}
        {@const dotnetStats = ext.name === 'dotnet-review' ? parseDotnetReview(run?.statsJson ?? null) : null}
        {@const running = runningExtId === ext.id}
        {@const runError = runErrors[ext.id] ?? null}
        <div class="space-y-4">
          <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? ext.name : ext.name.toUpperCase()}</h2>
                {#if ext.description}
                  <p class="mt-1 text-subink" style="font-size:12.5px">{ext.description}</p>
                {/if}
                <p class="mt-1 text-subink" style="font-size:12.5px">
                  Target branch: <span class="text-ink" style="font-family:var(--font-mono); font-size:12px">{mr.sourceBranch}</span>.
                  {#if run}
                    <span class="text-mute"> — last run {timeAgo(run.startedAt)}</span>
                  {/if}
                </p>
                {#if run}
                  <div class="mt-2 flex items-center gap-2">
                    <Pill tone={statusTone(run.status)}>
                      {run.status}{run.exitCode !== null ? ` (${run.exitCode})` : ''}
                    </Pill>
                    {#if run.branch !== mr.sourceBranch}
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
                  disabled={running}
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
                mrProjectId={projectId}
                mrIid={mr.iid}
                mrWebUrl={mr.webUrl}
                workProjectId={projectId}
                workBranch={mr.sourceBranch}
                returnListParent={listParent}
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
                Run {ext.name} to produce a report for this branch.
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
