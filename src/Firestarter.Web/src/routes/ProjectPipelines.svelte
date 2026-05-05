<script lang="ts">
  import {
    api,
    type ProjectDetail,
    type ExtensionView,
    type JenkinsPipelineItem,
  } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { workspaceOpen } from '../lib/stores/workspaceOpenContext.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import { kpiNumeralFontWeightCss } from '../lib/theme/chrome';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import ProjectViewTabs from '../lib/components/ProjectViewTabs.svelte';
  import JenkinsPipelineList from '../lib/components/JenkinsPipelineList.svelte';
  import { timeAgo, splitPath } from '../lib/format';
  import { isProjectBranchMrSyncFresh } from '../lib/gitlabSyncFreshness';
  import {
    recordJenkinsPipelinesLiveFetch,
    shouldAutoFetchJenkinsPipelinesFromLive,
  } from '../lib/jenkinsPipelinesRefresh';

  interface Props {
    projectId: number;
  }
  let { projectId }: Props = $props();

  let project = $state<ProjectDetail | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);
  let projectExtensions = $state<ExtensionView[]>([]);

  let jenkinsPipelines = $state<JenkinsPipelineItem[]>([]);
  let jenkinsPipelinesConfigured = $state(false);
  let jenkinsPipelinesError = $state<string | null>(null);

  /** Live Jenkins list + extension metadata refresh after showing DB snapshot */
  let pipelinesRefreshing = $state(false);
  /** Background GitLab sync (branches / MRs) */
  let remoteSyncBusy = $state(false);

  let loadGeneration = 0;

  $effect(() => {
    projectId;
    void load();
  });

  $effect(() => {
    if (project) {
      workspaceOpen.set({ projectId: project.id, branch: project.defaultBranch ?? null });
    } else {
      workspaceOpen.set(null);
    }
    return () => workspaceOpen.set(null);
  });

  function extTabId(id: number) {
    return `ext-${id}`;
  }

  function tabLabel(ext: ExtensionView): string {
    const t = ext.targets.find(x => x.view === 'project');
    return t?.label && t.label.trim() !== '' ? t.label : ext.name;
  }

  const parts = $derived(project ? splitPath(project.pathWithNamespace) : { ns: '', name: '' });
  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';

  const tabs = $derived(
    project
      ? [
          { id: 'overview' as string, label: 'Overview' },
          { id: 'branches' as string, label: 'Branches' },
          { id: 'merge-requests' as string, label: 'Merge requests' },
          { id: 'pipelines' as string, label: 'Pipelines' },
          ...projectExtensions.map(e => ({ id: extTabId(e.id), label: tabLabel(e) })),
        ]
      : [],
  );

  const statCells = $derived(
    project
      ? [
          { label: 'Default', value: project.defaultBranch ?? '—', mono: true, accent: false },
          { label: 'Branches', value: String(project.branches.length), mono: false, accent: false },
          {
            label: 'Open MRs',
            value: String(project.openMergeRequests.length),
            mono: false,
            accent: project.openMergeRequests.length > 0,
          },
          {
            label: 'Drafts',
            value: String(project.openMergeRequests.filter(m => m.draft).length),
            mono: false,
            accent: false,
          },
          { label: 'Archived', value: project.archived ? 'yes' : 'no', mono: false, accent: false },
          { label: 'Activity', value: timeAgo(project.lastActivityAt), mono: false, accent: false },
        ]
      : [],
  );

  const chrome = $derived(theme.chrome);
  const kpiNumWeight = $derived(kpiNumeralFontWeightCss(chrome));

  function onTabSelect(id: string) {
    if (!project) return;
    if (id === 'pipelines') {
      route.goProjectPipelines(project.id);
      return;
    }
    if (id.startsWith('ext-')) {
      route.goProject(project.id, id);
      return;
    }
    if (id === 'overview' || id === 'branches' || id === 'merge-requests') {
      route.goProject(project.id, id);
      return;
    }
  }

  const pipelineListTitle = $derived(
    chrome.sectionLabelCasing === 'sentence' ? 'All pipelines' : 'ALL PIPELINES',
  );

  function sleep(ms: number) {
    return new Promise<void>(resolve => setTimeout(resolve, ms));
  }

  function applyJenkinsSnapshotFromProject(p: ProjectDetail) {
    jenkinsPipelinesConfigured = p.jenkinsPipelinesConfigured ?? false;
    const c = p.jenkinsPipelinesCache ?? null;
    jenkinsPipelines =
      c !== null && c.length > 0 ? c : c !== null ? c : [];
    jenkinsPipelinesError = null;
  }

  /** After sync worker finishes, merge fresh DB rows (and Jenkins list when idle). */
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
        applyJenkinsSnapshotFromProject(projRes.project);
      }
      if (shouldAutoFetchJenkinsPipelinesFromLive(pid)) {
        try {
          const plRes = await api.listProjectPipelines(pid, 50);
          if (gen !== loadGeneration) return;
          jenkinsPipelines = plRes.pipelines ?? [];
          jenkinsPipelinesConfigured = plRes.jenkinsConfigured;
          jenkinsPipelinesError = plRes.error ?? null;
          recordJenkinsPipelinesLiveFetch(pid);
        } catch {
          /* optional */
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
    pipelinesRefreshing = false;
    try {
      const projRes = await api.getProject(projectId);
      if (gen !== loadGeneration) return;
      project = projRes?.project ?? null;
      if (project) {
        applyJenkinsSnapshotFromProject(project);
        if (!isProjectBranchMrSyncFresh(project.branchesMrsSyncedAt)) {
          void api.startSync({ scope: 'Project', projectId, reason: 'project-pipelines-open' }).catch(() => {});
          void refreshProjectWhenSyncIdle(gen, projectId);
        }
      } else {
        jenkinsPipelines = [];
        jenkinsPipelinesConfigured = false;
        jenkinsPipelinesError = null;
      }
      loading = false;

      if (!project) return;

      try {
        const extRes = await api.listExtensions();
        if (gen !== loadGeneration) return;
        projectExtensions = extRes.extensions.filter(
          e => e.isEnabled && e.targets.some(t => t.view === 'project'),
        );
      } catch {
        /* extension tabs optional */
      }

      if (shouldAutoFetchJenkinsPipelinesFromLive(projectId)) {
        pipelinesRefreshing = true;
        try {
          const plRes = await api.listProjectPipelines(projectId, 50);
          if (gen !== loadGeneration) return;
          jenkinsPipelines = plRes.pipelines ?? [];
          jenkinsPipelinesConfigured = plRes.jenkinsConfigured;
          jenkinsPipelinesError = plRes.error ?? null;
          recordJenkinsPipelinesLiveFetch(projectId);
        } catch {
          /* keep DB snapshot from getProject */
        } finally {
          if (gen === loadGeneration) pipelinesRefreshing = false;
        }
      }
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      if (gen === loadGeneration) loading = false;
    }
  }

  async function manualRefreshJenkins() {
    if (!project) return;
    const gen = loadGeneration;
    pipelinesRefreshing = true;
    try {
      const plRes = await api.listProjectPipelines(projectId, 50);
      if (gen !== loadGeneration) return;
      jenkinsPipelines = plRes.pipelines ?? [];
      jenkinsPipelinesConfigured = plRes.jenkinsConfigured;
      jenkinsPipelinesError = plRes.error ?? null;
      recordJenkinsPipelinesLiveFetch(projectId);
    } catch {
      /* keep existing list */
    } finally {
      if (gen === loadGeneration) pipelinesRefreshing = false;
    }
  }
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
      breadcrumbSyncing={remoteSyncBusy || pipelinesRefreshing}
      crumbs={[
        { segment: 'projects', onNavigate: () => route.goProjects() },
        ...parts.ns.replace(/\/$/, '').split('/').filter(Boolean),
        parts.name,
      ]}
      title={parts.name}
    />

    <ProjectViewTabs {tabs} activeId="pipelines" onSelect={onTabSelect} {chrome} />

    <div class="grid grid-cols-6 gap-[1px] bg-hair">
      {#each statCells as c (c.label)}
        <div class="flex flex-col gap-1 bg-paper px-4 py-3">
          <div style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? c.label : c.label.toUpperCase()}</div>
          <div
            class:text-accent={c.accent}
            class:text-ink={!c.accent}
            style="font-family:{c.mono || !chrome.pageHeaderSerifTitle
              ? 'var(--font-mono)'
              : 'var(--font-serif)'}; font-size:{chrome.pageHeaderSerifTitle ? '20px' : '18px'}; font-weight:{kpiNumWeight}; letter-spacing:-.01em"
          >
            {c.value}
          </div>
        </div>
      {/each}
    </div>

    <div class="min-h-0 flex-1 overflow-auto p-6 bg-bg">
      <div
        class="border border-hair bg-paper p-5"
        style="border-radius:var(--radius-lg); border-width:{chrome.warmTitleBar ? '0.5px' : '1px'}"
      >
        <div class="mb-3 flex items-start justify-between gap-3">
          <h2 style={labelFont}>{pipelineListTitle}</h2>
          <button
            type="button"
            class="shrink-0 rounded border border-hair bg-paper px-2.5 py-1 text-[12px] text-ink transition-colors hover:bg-panel-2 disabled:opacity-50"
            style="font-family:var(--font-mono); border-width:{chrome.warmTitleBar ? '0.5px' : '1px'}"
            disabled={pipelinesRefreshing}
            onclick={manualRefreshJenkins}
          >
            Refresh
          </button>
        </div>
        <JenkinsPipelineList
          includeHeading={false}
          pipelines={jenkinsPipelines}
          jenkinsConfigured={jenkinsPipelinesConfigured}
          error={jenkinsPipelinesError}
        />
      </div>
    </div>
  {/if}
</div>
