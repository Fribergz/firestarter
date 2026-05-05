<script lang="ts">
  import Pill from './Pill.svelte';
  import { theme } from '../stores/theme.svelte';
  import { route } from '../stores/route.svelte';
  import { api } from '../api';
  import { marked } from 'marked';

  export type DotnetReviewDiagnostic = {
    file: string;
    line: number;
    column: number;
    severity: 'error' | 'warning' | 'info';
    code: string;
    message: string;
    project: string | null;
  };

  export type DotnetReviewPackage = {
    project: string;
    framework: string;
    id: string;
    requested: string;
    current: string;
    latest: string;
  };

  export type DotnetReviewTestFailure = {
    name: string;
    outcome: string;
    duration: string | null;
    message: string;
  };

  export type DotnetReviewStats = {
    schema?: string;
    target?: string;
    error?: string;
    summary?: {
      buildSucceeded: boolean;
      testsPassed: boolean;
      analyzerErrors: number;
      analyzerWarnings: number;
      outdatedCount: number;
    };
    build?: {
      restoreExit: number;
      exitCode: number;
      succeeded: boolean;
      errors: number;
      warnings: number;
      info: number;
    };
    tests?: {
      ran: boolean;
      exitCode: number | null;
      total: number;
      passed: number;
      failed: number;
      skipped: number;
      durationMs: number;
      failures: DotnetReviewTestFailure[];
    };
    outdated?: {
      count: number;
      error: string | null;
      packages: DotnetReviewPackage[];
    };
    analyzers?: {
      errors: DotnetReviewDiagnostic[];
      warnings: DotnetReviewDiagnostic[];
      info: DotnetReviewDiagnostic[];
      byCode: { code: string; count: number }[];
    };
  };

  interface Props {
    stats: DotnetReviewStats;
    /** Comma-separated diagnostic codes to hide (from the dotnet-review extension's `hiddenCodes` setting). */
    hiddenCodes?: string;
    /** When set, enables the "Compose Thread" wizard that posts a top-level discussion to the given MR. */
    mrProjectId?: number | null;
    mrIid?: number | null;
    mrWebUrl?: string | null;
    /** Firestarter project id — enables the Run tab to `dotnet run` the cloned repo (MR branch or default). */
    workProjectId?: number | null;
    workBranch?: string | null;
    /** After a successful "Post thread" from the compose tab, navigate like the MR breadcrumb (reviewing queue vs project). */
    returnListParent?: 'project' | 'reviewing' | null;
  }
  let {
    stats,
    hiddenCodes = '',
    mrProjectId = null,
    mrIid = null,
    mrWebUrl = null,
    workProjectId = null,
    workBranch = null,
    returnListParent = null,
  }: Props = $props();
  const canCompose = $derived(mrProjectId != null && mrIid != null);
  const canRun = $derived(workProjectId != null);

  $effect(() => {
    if (section === 'compose' && !canCompose) section = 'overview';
  });
  $effect(() => {
    if (section === 'run' && !canRun) section = 'overview';
  });

  const hiddenCodeSet = $derived(new Set(
    (hiddenCodes ?? '')
      .split(',')
      .map(s => s.trim())
      .filter(s => s.length > 0)
      .map(s => s.toUpperCase()),
  ));

  function keepDiag(d: DotnetReviewDiagnostic): boolean {
    return !hiddenCodeSet.has((d.code ?? '').toUpperCase());
  }

  type SectionId = 'overview' | 'analyzers' | 'tests' | 'outdated' | 'run' | 'compose';

  let section = $state<SectionId>('overview');

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const monoSmall = 'font-family:var(--font-mono); font-size:11.5px';

  const mainSections: { id: 'overview' | 'analyzers' | 'tests' | 'outdated'; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'analyzers', label: 'Analyzers' },
    { id: 'tests', label: 'Tests' },
    { id: 'outdated', label: 'Outdated' },
  ];

  function shortFile(path: string): string {
    if (!path) return '';
    const parts = path.replace(/\\/g, '/').split('/');
    return parts.slice(-2).join('/');
  }

  function fmtDuration(ms: number): string {
    if (!ms) return '—';
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    const m = Math.floor(ms / 60000);
    const s = Math.floor((ms % 60000) / 1000);
    return `${m}m${s.toString().padStart(2, '0')}s`;
  }

  const summary = $derived(stats.summary);
  const build = $derived(stats.build);
  const tests = $derived(stats.tests);
  const outdated = $derived(stats.outdated);
  /** Filtered analyzer view — diagnostic codes in `hiddenCodes` are stripped, and `byCode` is recomputed. */
  const analyzers = $derived.by(() => {
    const a = stats.analyzers;
    if (!a) return undefined;
    const errors = (a.errors ?? []).filter(keepDiag);
    const warnings = (a.warnings ?? []).filter(keepDiag);
    const info = (a.info ?? []).filter(keepDiag);
    const counts = new Map<string, number>();
    for (const d of [...errors, ...warnings, ...info]) {
      counts.set(d.code, (counts.get(d.code) ?? 0) + 1);
    }
    const byCode = [...counts.entries()]
      .map(([code, count]) => ({ code, count }))
      .sort((x, y) => y.count - x.count);
    return { errors, warnings, info, byCode };
  });
  /** Counts after filtering — overrides the script's pre-filter summary so the overview reflects the user's view. */
  const filteredCounts = $derived({
    errors: analyzers?.errors.length ?? 0,
    warnings: analyzers?.warnings.length ?? 0,
    info: analyzers?.info.length ?? 0,
  });
  const chrome = $derived(theme.chrome);

  // ---------------------------------------------------------------------------
  // Compose Thread wizard (section === 'compose')
  // ---------------------------------------------------------------------------
  let includeBuild = $state(true);
  let includeTests = $state(true);
  /** Diagnostic key = severity:code:file:line — stable across re-renders. */
  let selectedDiagKeys = $state<Set<string>>(new Set());
  /** Per outdated package row: project + framework + id. */
  let selectedOutdatedKeys = $state<Set<string>>(new Set());
  let posting = $state(false);
  let postError = $state<string | null>(null);
  let postedNoteUrl = $state<string | null>(null);

  function pkgKey(p: { project: string; framework: string; id: string }): string {
    return `${p.project}\0${p.framework}\0${p.id}`;
  }

  /** Logical identity — two analyzer rows can still collide (e.g. duplicate build outputs). */
  function diagKey(d: DotnetReviewDiagnostic): string {
    return `${d.severity}:${d.code}:${d.file}:${d.line}:${d.column}`;
  }

  /** Stable unique key for UI state and {#each} (index disambiguates duplicate diagKey rows). */
  function diagRowKey(d: DotnetReviewDiagnostic, index: number): string {
    return `${index}::${diagKey(d)}`;
  }

  function allDiagnosticsFlat(): DotnetReviewDiagnostic[] {
    return [
      ...(analyzers?.errors ?? []),
      ...(analyzers?.warnings ?? []),
      ...(analyzers?.info ?? []),
    ];
  }

  function toggleDiag(key: string, on: boolean) {
    const next = new Set(selectedDiagKeys);
    if (on) next.add(key); else next.delete(key);
    selectedDiagKeys = next;
  }

  function selectAllDiags() {
    const next = new Set<string>();
    allDiagnosticsFlat().forEach((d, i) => next.add(diagRowKey(d, i)));
    selectedDiagKeys = next;
  }

  function clearDiags() {
    selectedDiagKeys = new Set();
  }

  function selectAllOutdated() {
    const next = new Set<string>();
    for (const p of outdated?.packages ?? []) {
      next.add(pkgKey(p));
    }
    selectedOutdatedKeys = next;
  }

  function clearOutdated() {
    selectedOutdatedKeys = new Set();
  }

  function toggleOutdatedKey(key: string, on: boolean) {
    const next = new Set(selectedOutdatedKeys);
    if (on) next.add(key);
    else next.delete(key);
    selectedOutdatedKeys = next;
  }

  function initComposeDefaults() {
    selectAllDiags();
    includeBuild = !!build && !build.succeeded;
    includeTests = !!tests?.ran && tests.failed > 0;
    selectAllOutdated();
    postError = null;
    postedNoteUrl = null;
  }

  function onSelectComposeTab() {
    if (!canCompose) return;
    initComposeDefaults();
    section = 'compose';
  }

  function escapePipes(s: string): string {
    return (s ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
  }

  /** Build the GitLab-flavoured markdown body from current selections. */
  const composedBody = $derived.by(() => {
    if (section !== 'compose' || !canCompose) return '';
    const parts: string[] = [];

    if (includeBuild && build) {
      parts.push('### Build');
      const status = build.succeeded ? ':white_check_mark: succeeded' : `:x: failed (exit ${build.exitCode})`;
      parts.push(`- Status: ${status}`);
      parts.push(`- Errors: **${filteredCounts.errors}** · Warnings: **${filteredCounts.warnings}** · Info: **${filteredCounts.info}**`);
      parts.push('');
    }

    if (includeTests && tests) {
      parts.push('### Tests');
      if (!tests.ran) {
        parts.push('- _Skipped_ — build did not succeed.');
      } else {
        parts.push(`- ${tests.passed}/${tests.total} passed · ${tests.failed} failed · ${tests.skipped} skipped · ${fmtDuration(tests.durationMs)}`);
        if (tests.failures && tests.failures.length > 0) {
          parts.push('');
          parts.push('| Test | Outcome | Message |');
          parts.push('|------|---------|---------|');
          for (const f of tests.failures.slice(0, 25)) {
            parts.push(`| ${escapePipes(f.name)} | ${escapePipes(f.outcome)} | ${escapePipes((f.message ?? '').slice(0, 200))} |`);
          }
          if (tests.failures.length > 25) {
            parts.push(`| _…and ${tests.failures.length - 25} more_ | | |`);
          }
        }
      }
      parts.push('');
    }

    const picked = allDiagnosticsFlat().filter((d, i) => selectedDiagKeys.has(diagRowKey(d, i)));

    if (picked.length > 0) {
      parts.push(`### Analyzer diagnostics (${picked.length})`);
      parts.push('');
      parts.push('| Severity | Code | Message | Location |');
      parts.push('|----------|------|---------|----------|');
      for (const d of picked) {
        const sev = d.severity === 'error' ? 'error'
          : d.severity === 'warning' ? 'warn'
          : 'info';
        parts.push(`| ${sev} | \`${escapePipes(d.code)}\` | ${escapePipes(d.message)} | \`${escapePipes(shortFile(d.file))}:${d.line}\` |`);
      }
      parts.push('');
    }

    const outdatedPicked = (outdated?.packages ?? []).filter(p => selectedOutdatedKeys.has(pkgKey(p)));
    if (outdatedPicked.length > 0) {
      parts.push(`### Outdated NuGet packages (${outdatedPicked.length})`);
      parts.push('');
      parts.push('| Package | Project | Current | Latest |');
      parts.push('|---------|---------|---------|--------|');
      for (const p of outdatedPicked) {
        parts.push(`| \`${escapePipes(p.id)}\` | ${escapePipes(p.project)} | ${escapePipes(p.current)} | ${escapePipes(p.latest)} |`);
      }
      parts.push('');
    }

    return parts.join('\n').trimEnd() + '\n';
  });

  /** Rendered HTML for the right-pane preview (GFM via `marked`). */
  const composedHtml = $derived.by(() => {
    if (section !== 'compose' || !canCompose) return '';
    return marked.parse(composedBody, { gfm: true, breaks: false, async: false }) as string;
  });

  async function postThread() {
    if (mrProjectId == null || mrIid == null) return;
    posting = true;
    postError = null;
    postedNoteUrl = null;
    try {
      const res = await api.postMrDiscussion(mrProjectId, mrIid, composedBody);
      postedNoteUrl = mrWebUrl ? `${mrWebUrl}${res.webUrl ?? ''}` : null;
      if (returnListParent === 'reviewing') {
        route.goMrReviewer();
      } else if (returnListParent === 'project' && workProjectId != null) {
        route.goProject(workProjectId);
      }
    } catch (err) {
      postError = err instanceof Error ? err.message : String(err);
    } finally {
      posting = false;
    }
  }

  // —— Interactive `dotnet run` (manual smoke test) ——
  let runBusy = $state(false);
  let runError = $state<string | null>(null);
  let interactiveRunning = $state(false);
  let interactivePid = $state<number | null>(null);

  async function refreshRunStatus() {
    try {
      const s = await api.dotnetInteractiveRunStatus();
      interactiveRunning = s.running;
      interactivePid = s.pid ?? null;
    } catch {
      interactiveRunning = false;
      interactivePid = null;
    }
  }

  $effect(() => {
    if (section !== 'run' || !canRun) return;
    void refreshRunStatus();
    const t = setInterval(() => void refreshRunStatus(), 2000);
    return () => clearInterval(t);
  });

  async function startInteractiveRun() {
    if (workProjectId == null) return;
    runBusy = true;
    runError = null;
    try {
      const r = await api.dotnetInteractiveRunStart({
        projectId: workProjectId,
        branch: workBranch ?? null,
        targetPath: stats.target ?? null,
      });
      if (!r.ok) runError = r.error ?? 'Could not start dotnet run';
    } catch (e) {
      runError = e instanceof Error ? e.message : String(e);
    } finally {
      runBusy = false;
      void refreshRunStatus();
    }
  }

  async function stopInteractiveRun() {
    runBusy = true;
    runError = null;
    try {
      const r = await api.dotnetInteractiveRunStop();
      if (!r.ok) runError = r.error ?? 'Could not stop the process';
    } catch (e) {
      runError = e instanceof Error ? e.message : String(e);
    } finally {
      runBusy = false;
      void refreshRunStatus();
    }
  }
</script>

{#if stats.error}
  <div class="border border-danger p-3 text-danger"
       style="border-radius:var(--radius); background:var(--danger-soft); font-size:12.5px">
    {stats.error}
  </div>
{:else}
  <div class="space-y-3">
    <div class="flex flex-wrap items-center gap-2 border-b border-hair pb-2">
      {#each mainSections as s (s.id)}
        {@const active = section === s.id}
        <button
          type="button"
          onclick={() => (section = s.id)}
          class="px-3 py-1 text-[11.5px]"
          class:bg-panel-2={active}
          class:text-ink={active}
          class:text-subink={!active}
          style="border-radius:var(--radius); font-family:var(--font-mono); letter-spacing:.06em"
        >
          {chrome.sectionLabelCasing === 'sentence' ? s.label : s.label.toUpperCase()}
        </button>
      {/each}
      {#if canRun}
        <button
          type="button"
          onclick={() => (section = 'run')}
          class="px-3 py-1 text-[11.5px]"
          class:bg-panel-2={section === 'run'}
          class:text-ink={section === 'run'}
          class:text-subink={section !== 'run'}
          style="border-radius:var(--radius); font-family:var(--font-mono); letter-spacing:.06em"
        >
          {chrome.sectionLabelCasing === 'sentence' ? 'Run' : 'RUN'}
        </button>
      {/if}
      {#if canCompose}
        <span
          class="mx-0.5 h-5 w-px shrink-0 self-center bg-hair"
          style="min-height:1rem"
          aria-hidden="true"
        ></span>
        <button
          type="button"
          onclick={onSelectComposeTab}
          class="px-3 py-1 text-[11.5px]"
          class:bg-panel-2={section === 'compose'}
          class:text-ink={section === 'compose'}
          class:text-subink={section !== 'compose'}
          style="border-radius:var(--radius); font-family:var(--font-mono); letter-spacing:.06em"
        >
          {chrome.sectionLabelCasing === 'sentence' ? 'Compose thread' : 'COMPOSE THREAD'}
        </button>
      {/if}
      {#if stats.target}
        <span class="ml-auto truncate text-mute" style="{monoSmall}; max-width:420px" title={stats.target}>
          {shortFile(stats.target)}
        </span>
      {/if}
    </div>

    {#if section === 'compose' && canCompose}
      {@const allDiags = [...(analyzers?.errors ?? []), ...(analyzers?.warnings ?? []), ...(analyzers?.info ?? [])]}
      <div class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
        <div class="flex items-center justify-between gap-3 border-b border-hair px-4 py-2">
          <div class="text-ink" style="font-size:12.5px; font-weight:500">Compose thread</div>
          <div class="text-mute" style={monoSmall}>top-level discussion · MR !{mrIid}</div>
        </div>

        <div class="grid items-stretch gap-0 md:grid-cols-2">
          <!-- LEFT: include — build/tests toggles, outdated rows, diagnostics (stretches to match preview) -->
          <div class="flex min-h-0 min-w-0 flex-col border-b border-hair p-3 md:border-b-0 md:border-r">
            <div class="mb-2" style={labelFont}>INCLUDE</div>
            <label class="flex items-center gap-2 text-subink" style="font-size:12.5px">
              <input type="checkbox" bind:checked={includeBuild} disabled={!build} />
              Build summary {#if build}<span class="text-mute" style={monoSmall}>· {build.succeeded ? 'ok' : `exit ${build.exitCode}`}</span>{/if}
            </label>
            <label class="mt-1 flex items-center gap-2 text-subink" style="font-size:12.5px">
              <input type="checkbox" bind:checked={includeTests} disabled={!tests} />
              Tests summary {#if tests?.ran}<span class="text-mute" style={monoSmall}>· {tests.passed}/{tests.total} ({tests.failed} failed)</span>{/if}
            </label>

            {#if outdated && outdated.packages.length > 0}
              <div class="mt-3 flex items-center justify-between gap-2">
                <div style={labelFont}>OUTDATED ({outdated.packages.length})</div>
                <div class="flex items-center gap-1">
                  <button type="button" onclick={selectAllOutdated} class="text-mute hover:text-ink" style={monoSmall}>all</button>
                  <span class="text-mute">·</span>
                  <button type="button" onclick={clearOutdated} class="text-mute hover:text-ink" style={monoSmall}>none</button>
                </div>
              </div>
              <div class="mt-2 overflow-x-auto border border-hair" style="border-radius:var(--radius)">
                <table class="w-full min-w-[440px] border-collapse">
                  <thead>
                    <tr class="border-b border-hair text-left" style={labelFont}>
                      <th class="w-9 py-1.5 pl-2 pr-0 align-middle"></th>
                      <th class="py-1.5 pr-2 align-middle">
                        {chrome.sectionLabelCasing === 'sentence' ? 'Package' : 'PACKAGE'}
                      </th>
                      <th class="py-1.5 pr-2 align-middle">
                        {chrome.sectionLabelCasing === 'sentence' ? 'Project' : 'PROJECT'}
                      </th>
                      <th class="py-1.5 pr-2 align-middle">
                        {chrome.sectionLabelCasing === 'sentence' ? 'Current' : 'CURRENT'}
                      </th>
                      <th class="py-1.5 pr-2 align-middle">
                        {chrome.sectionLabelCasing === 'sentence' ? 'Latest' : 'LATEST'}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {#each outdated.packages as p, i (`${p.project}-${p.id}-${i}`)}
                      {@const pk = pkgKey(p)}
                      <tr class="border-b border-hair">
                        <td class="py-1.5 pl-2 align-middle">
                          <input
                            type="checkbox"
                            class="shrink-0"
                            checked={selectedOutdatedKeys.has(pk)}
                            onchange={(e) => toggleOutdatedKey(pk, (e.currentTarget as HTMLInputElement).checked)}
                          />
                        </td>
                        <td class="py-1.5 pr-2 align-middle text-ink" style="font-size:12.5px">{p.id}</td>
                        <td class="max-w-[140px] truncate py-1.5 pr-2 align-middle text-subink" style={monoSmall} title={p.project}>
                          {p.project}
                        </td>
                        <td class="py-1.5 pr-2 align-middle text-mute" style={monoSmall}>{p.current}</td>
                        <td class="py-1.5 pr-2 align-middle text-accent" style={monoSmall}>{p.latest}</td>
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
            {/if}

            <div class="mt-3 flex items-center gap-3">
              <div style={labelFont}>DIAGNOSTICS ({allDiags.length})</div>
              <button type="button" onclick={selectAllDiags} class="text-mute hover:text-ink" style={monoSmall}>all</button>
              <span class="text-mute">·</span>
              <button type="button" onclick={clearDiags} class="text-mute hover:text-ink" style={monoSmall}>none</button>
            </div>
            {#if allDiags.length === 0}
              <p class="mt-2 text-mute" style="font-size:12px">No diagnostics to include.</p>
            {:else}
              <div class="mt-2 border border-hair" style="border-radius:var(--radius)">
                <ul class="divide-y divide-hair">
                  {#each allDiags as d, i (diagRowKey(d, i))}
                    {@const key = diagRowKey(d, i)}
                    <li class="flex items-start gap-2 px-2 py-1.5">
                      <input
                        type="checkbox"
                        class="mt-1 shrink-0"
                        checked={selectedDiagKeys.has(key)}
                        onchange={(e) => toggleDiag(key, (e.currentTarget as HTMLInputElement).checked)}
                      />
                      <div class="min-w-0 flex-1">
                        <div class="flex items-center gap-2">
                          <Pill tone={d.severity === 'error' ? 'failed' : d.severity === 'warning' ? 'draft' : 'neutral'}>
                            {d.severity}
                          </Pill>
                          <span class="text-subink" style={monoSmall}>{d.code}</span>
                          <span class="truncate text-mute" style={monoSmall} title={d.file}>{shortFile(d.file)}:{d.line}</span>
                        </div>
                        <div class="truncate text-ink" style="font-size:12px" title={d.message}>{d.message}</div>
                      </div>
                    </li>
                  {/each}
                </ul>
              </div>
            {/if}
          </div>

          <!-- RIGHT: preview -->
          <div class="flex min-h-0 min-w-0 flex-col p-3">
            <div class="mb-2 flex items-center justify-between">
              <div style={labelFont}>PREVIEW</div>
              <div class="text-mute" style={monoSmall}>{composedBody.length} chars</div>
            </div>
            <div
              class="gitlab-md border border-hair bg-bg p-3 text-ink"
              style="border-radius:var(--radius); font-size:13px; line-height:1.5"
            >
              {@html composedHtml}
            </div>
            <details class="mt-2">
              <summary class="cursor-pointer text-mute hover:text-ink" style={monoSmall}>view raw markdown</summary>
              <pre
                class="mt-2 overflow-x-auto bg-panel-2 p-2 text-subink"
                style="border-radius:var(--radius); font-family:var(--font-mono); font-size:11px; white-space:pre-wrap"
              >{composedBody}</pre>
            </details>
          </div>
        </div>

        <div class="flex flex-wrap items-center gap-3 border-t border-hair px-4 py-3">
          <div class="flex min-w-0 flex-1 flex-wrap items-center gap-2">
            {#if postedNoteUrl}
              <span class="text-ok" style="font-size:12px">Posted.</span>
              <a href={postedNoteUrl} target="_blank" rel="noreferrer" class="text-accent" style="font-family:var(--font-mono); font-size:11.5px">
                view on GitLab ↗
              </a>
            {:else if postError}
              <span class="text-danger" style="font-size:12px">{postError}</span>
            {/if}
          </div>
          <button
            type="button"
            onclick={postThread}
            disabled={posting || composedBody.trim().length === 0}
            class="shrink-0 border border-accent px-3 py-1.5 text-[12px] disabled:opacity-50"
            style="border-radius:var(--radius); color:var(--paper); background:var(--accent)"
          >
            {posting ? 'Posting…' : 'Post thread'}
          </button>
        </div>
      </div>
    {:else if section === 'run' && canRun}
      <div class="border border-hair bg-paper p-4" style="border-radius:var(--radius-lg)">
        <p class="text-subink" style="font-size:12.5px">
          Starts <span style={monoSmall}>dotnet run</span> for the same solution or project the report used, on branch
          <span class="text-ink" style={monoSmall}>{workBranch ?? '(default)'}</span>.
          A console window opens; stop here when you are done (or close that window).
        </p>
        <div class="mt-3 flex flex-wrap items-center gap-2">
          <button
            type="button"
            disabled={runBusy || interactiveRunning}
            onclick={startInteractiveRun}
            class="border border-accent px-3 py-1.5 text-[12px] disabled:opacity-50"
            style="border-radius:var(--radius); color:var(--paper); background:var(--accent)"
          >
            {runBusy ? 'Starting…' : 'Start dotnet run'}
          </button>
          <button
            type="button"
            disabled={runBusy || !interactiveRunning}
            onclick={stopInteractiveRun}
            class="border border-hair px-3 py-1.5 text-[12px] text-ink disabled:opacity-50"
            style="border-radius:var(--radius); background:var(--panel-2)"
          >
            {runBusy && !interactiveRunning ? '…' : 'Stop'}
          </button>
          <span class="text-mute" style={monoSmall}>
            {#if interactiveRunning}
              Running{#if interactivePid != null} (pid {interactivePid}){/if}
            {:else}
              Not running
            {/if}
          </span>
        </div>
        {#if runError}
          <p class="mt-2 text-danger" style="font-size:12.5px">{runError}</p>
        {/if}
      </div>
    {:else if section === 'overview'}
      <div class="grid grid-cols-2 gap-3 md:grid-cols-4">
        <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
          <div style={labelFont}>BUILD</div>
          <div class="mt-1 flex items-center gap-2">
            <Pill tone={build?.succeeded ? 'passed' : 'failed'}>
              {build?.succeeded ? 'ok' : `exit ${build?.exitCode ?? '?'}`}
            </Pill>
          </div>
          <div class="mt-2 text-mute" style={monoSmall}>
            {filteredCounts.errors} errors · {filteredCounts.warnings} warnings · {filteredCounts.info} info
          </div>
        </div>
        <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
          <div style={labelFont}>TESTS</div>
          <div class="mt-1 flex items-center gap-2">
            {#if tests?.ran}
              <Pill tone={tests.failed === 0 && tests.total > 0 ? 'passed' : tests.total === 0 ? 'neutral' : 'failed'}>
                {tests.passed}/{tests.total}
              </Pill>
            {:else}
              <Pill tone="neutral">skipped</Pill>
            {/if}
          </div>
          <div class="mt-2 text-mute" style={monoSmall}>
            {#if tests?.ran}
              {tests.failed} failed · {tests.skipped} skipped · {fmtDuration(tests.durationMs)}
            {:else}
              build did not succeed
            {/if}
          </div>
        </div>
        <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
          <div style={labelFont}>ANALYZERS</div>
          <div class="mt-1 text-ink" style="font-size:18px">
            {filteredCounts.errors + filteredCounts.warnings + filteredCounts.info}
          </div>
          <div class="mt-1 text-mute" style={monoSmall}>
            {filteredCounts.errors} errors · {filteredCounts.warnings} warnings · {filteredCounts.info} info
          </div>
        </div>
        <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
          <div style={labelFont}>OUTDATED</div>
          <div class="mt-1 text-ink" style="font-size:18px">{outdated?.count ?? 0}</div>
          <div class="mt-1 text-mute" style={monoSmall}>nuget packages</div>
        </div>
      </div>

      {#if analyzers?.byCode && analyzers.byCode.length > 0}
        <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
          <div class="mb-2" style={labelFont}>TOP DIAGNOSTIC CODES</div>
          <div class="flex flex-wrap gap-2">
            {#each analyzers.byCode.slice(0, 10) as row (row.code)}
              <span class="border border-hair px-2 py-0.5 text-subink"
                    style="border-radius:var(--radius); {monoSmall}">
                {row.code} · {row.count}
              </span>
            {/each}
          </div>
        </div>
      {/if}
    {:else if section === 'analyzers'}
      {@const all = [...(analyzers?.errors ?? []), ...(analyzers?.warnings ?? []), ...(analyzers?.info ?? [])]}
      {#if all.length === 0}
        <p class="p-3 text-mute" style="font-size:12px">No analyzer diagnostics.</p>
      {:else}
        <div class="overflow-auto border border-hair bg-paper" style="border-radius:var(--radius); max-height:480px">
          <table class="w-full border-collapse">
            <thead class="sticky top-0 bg-paper">
              <tr class="border-b border-hair text-left" style={labelFont}>
                <th class="py-2 pl-3">SEV</th>
                <th class="py-2">CODE</th>
                <th class="py-2">MESSAGE</th>
                <th class="py-2">LOCATION</th>
              </tr>
            </thead>
            <tbody>
              {#each all as d, i (i)}
                <tr class="border-b border-hair">
                  <td class="py-1.5 pl-3 align-top">
                    <Pill tone={d.severity === 'error' ? 'failed' : d.severity === 'warning' ? 'draft' : 'neutral'}>
                      {d.severity}
                    </Pill>
                  </td>
                  <td class="py-1.5 pr-3 align-top text-subink" style={monoSmall}>{d.code}</td>
                  <td class="py-1.5 pr-3 align-top text-ink" style="font-size:12px">{d.message}</td>
                  <td class="py-1.5 pr-3 align-top text-mute" style={monoSmall} title={d.file}>
                    {shortFile(d.file)}:{d.line}
                  </td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    {:else if section === 'tests'}
      {#if !tests?.ran}
        <p class="p-3 text-mute" style="font-size:12px">
          Tests were not run (build did not succeed).
        </p>
      {:else}
        <div class="grid grid-cols-4 gap-3">
          <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
            <div style={labelFont}>TOTAL</div>
            <div class="text-ink" style="font-size:20px; font-family:var(--font-mono)">{tests.total}</div>
          </div>
          <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
            <div style={labelFont}>PASSED</div>
            <div class="text-ok" style="font-size:20px; font-family:var(--font-mono)">{tests.passed}</div>
          </div>
          <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
            <div style={labelFont}>FAILED</div>
            <div style="font-size:20px; font-family:var(--font-mono); color:{tests.failed > 0 ? 'var(--danger)' : 'var(--mute)'}">
              {tests.failed}
            </div>
          </div>
          <div class="border border-hair bg-paper p-3" style="border-radius:var(--radius)">
            <div style={labelFont}>SKIPPED</div>
            <div class="text-mute" style="font-size:20px; font-family:var(--font-mono)">{tests.skipped}</div>
          </div>
        </div>
        {#if tests.failures.length > 0}
          <div class="border border-hair bg-paper" style="border-radius:var(--radius)">
            <div class="border-b border-hair px-3 py-2" style={labelFont}>FAILURES</div>
            <ul class="divide-y divide-hair">
              {#each tests.failures as f, i (i)}
                <li class="px-3 py-2">
                  <div class="text-ink" style="font-size:12.5px; font-weight:500">{f.name}</div>
                  <div class="text-mute" style={monoSmall}>{f.outcome}{f.duration ? ` · ${f.duration}` : ''}</div>
                  {#if f.message}
                    <pre class="mt-1 overflow-x-auto bg-panel-2 p-2 text-danger"
                         style="border-radius:var(--radius); font-family:var(--font-mono); font-size:11px">{f.message}</pre>
                  {/if}
                </li>
              {/each}
            </ul>
          </div>
        {/if}
      {/if}
    {:else if section === 'outdated'}
      {#if outdated?.error}
        <p class="text-danger" style="font-size:12.5px">{outdated.error}</p>
      {:else if !outdated || outdated.packages.length === 0}
        <p class="p-3 text-mute" style="font-size:12px">All packages up to date.</p>
      {:else}
        <div class="overflow-auto border border-hair bg-paper" style="border-radius:var(--radius); max-height:480px">
          <table class="w-full border-collapse">
            <thead class="sticky top-0 bg-paper">
              <tr class="border-b border-hair text-left" style={labelFont}>
                <th class="py-2 pl-3">PACKAGE</th>
                <th class="py-2">PROJECT</th>
                <th class="py-2">TFM</th>
                <th class="py-2">CURRENT</th>
                <th class="py-2">LATEST</th>
              </tr>
            </thead>
            <tbody>
              {#each outdated.packages as p, i (`${p.project}-${p.id}-${i}`)}
                <tr class="border-b border-hair">
                  <td class="py-1.5 pl-3 text-ink" style="font-size:12.5px">{p.id}</td>
                  <td class="py-1.5 pr-3 text-subink" style={monoSmall}>{p.project}</td>
                  <td class="py-1.5 pr-3 text-mute" style={monoSmall}>{p.framework}</td>
                  <td class="py-1.5 pr-3 text-mute" style={monoSmall}>{p.current}</td>
                  <td class="py-1.5 pr-3 text-accent" style={monoSmall}>{p.latest}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    {/if}
  </div>
{/if}

<style>
  /* GitLab-flavoured preview — close enough to GitLab's default markdown rendering for review purposes. */
  .gitlab-md :global(h1),
  .gitlab-md :global(h2),
  .gitlab-md :global(h3),
  .gitlab-md :global(h4) {
    margin: 0.8em 0 0.4em;
    font-weight: 600;
    line-height: 1.3;
  }
  .gitlab-md :global(h1) { font-size: 1.4em; border-bottom: 1px solid var(--hair); padding-bottom: 0.2em; }
  .gitlab-md :global(h2) { font-size: 1.2em; border-bottom: 1px solid var(--hair); padding-bottom: 0.15em; }
  .gitlab-md :global(h3) { font-size: 1.05em; }
  .gitlab-md :global(p) { margin: 0.5em 0; }
  .gitlab-md :global(ul),
  .gitlab-md :global(ol) { margin: 0.4em 0 0.4em 1.4em; }
  .gitlab-md :global(li) { margin: 0.15em 0; }
  .gitlab-md :global(code) {
    font-family: var(--font-mono);
    background: var(--panel-2);
    padding: 0.05em 0.35em;
    border-radius: 3px;
    font-size: 0.9em;
  }
  .gitlab-md :global(pre) {
    background: var(--panel-2);
    padding: 0.6em 0.8em;
    border-radius: var(--radius);
    overflow-x: auto;
    font-size: 12px;
  }
  .gitlab-md :global(pre code) { background: transparent; padding: 0; }
  .gitlab-md :global(table) {
    border-collapse: collapse;
    margin: 0.6em 0;
    width: 100%;
    font-size: 12.5px;
  }
  .gitlab-md :global(th),
  .gitlab-md :global(td) {
    border: 1px solid var(--hair);
    padding: 0.35em 0.6em;
    text-align: left;
    vertical-align: top;
  }
  .gitlab-md :global(th) { background: var(--panel-2); font-weight: 600; }
  .gitlab-md :global(blockquote) {
    border-left: 3px solid var(--hair-strong);
    color: var(--subink);
    padding: 0.2em 0.8em;
    margin: 0.5em 0;
  }
  .gitlab-md :global(a) { color: var(--accent); }
</style>
