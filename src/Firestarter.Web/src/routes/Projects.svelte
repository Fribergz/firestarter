<script lang="ts">
  import { onMount } from 'svelte';
  import { api, type ProjectListItem } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import NavRail from '../lib/components/NavRail.svelte';
  import TableHeaderFilter, {
    type FilterState,
    type SortDir,
    isFilterActive,
  } from '../lib/components/TableHeaderFilter.svelte';
  import { timeAgo } from '../lib/format';

  type ColId = 'namespace' | 'project' | 'branches' | 'openMrs' | 'activity';

  let filter = $state('');
  let all = $state<ProjectListItem[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  let sortCol = $state<ColId | null>('activity');
  let sortDir = $state<'asc' | 'desc'>('desc');

  const emptyFilter: FilterState = { kind: 'none' };
  let fStarred = $state<FilterState>(emptyFilter);
  let fNamespace = $state<FilterState>(emptyFilter);
  let fBranches = $state<FilterState>(emptyFilter);
  let fOpenMrs = $state<FilterState>(emptyFilter);
  let fActivity = $state<FilterState>(emptyFilter);
  const starredOptions = ['starred', 'unstarred'];

  const chrome = $derived(theme.chrome);

  onMount(() => load());

  async function load() {
    loading = true;
    error = null;
    try {
      const res = await api.listProjects(null, 500);
      all = res.projects;
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      loading = false;
    }
  }

  const archivedExcluded = $derived(all.filter((p) => !p.archived));

  function namespaceOf(path: string): string {
    const i = path.lastIndexOf('/');
    return i < 0 ? '' : path.slice(0, i);
  }

  const namespaceOptions = $derived(
    Array.from(new Set(archivedExcluded.map((p) => namespaceOf(p.pathWithNamespace)))).sort(),
  );

  function dirFor(col: ColId): SortDir {
    return sortCol === col ? sortDir : null;
  }

  function toggleSort(col: ColId) {
    if (sortCol === col) {
      sortDir = sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      sortCol = col;
      sortDir = col === 'project' || col === 'namespace' ? 'asc' : 'desc';
    }
  }

  async function toggleStar(p: ProjectListItem, e: MouseEvent) {
    e.stopPropagation();
    const next = !p.starred;
    all = all.map((x) => (x.id === p.id ? { ...x, starred: next } : x));
    try {
      await api.setProjectStarred(p.id, next);
    } catch (err) {
      all = all.map((x) => (x.id === p.id ? { ...x, starred: !next } : x));
      error = err instanceof Error ? err.message : String(err);
    }
  }

  function matchSet(f: FilterState, value: string): boolean {
    if (f.kind !== 'set' || !isFilterActive(f)) return true;
    return f.selected.includes(value);
  }

  function matchNumber(f: FilterState, value: number): boolean {
    if (f.kind !== 'number' || !isFilterActive(f) || f.value === null) return true;
    if (f.op === 'eq') return value === f.value;
    if (f.op === 'lt') return value < f.value;
    return value > f.value;
  }

  function matchDate(f: FilterState, iso: string | null): boolean {
    if (f.kind !== 'date' || !isFilterActive(f)) return true;
    if (!iso) return false;
    const t = new Date(iso).getTime();
    const fromT = f.from ? new Date(f.from).getTime() : null;
    const toT = f.to ? new Date(f.to + 'T23:59:59').getTime() : null;
    if (f.op === 'before') return fromT !== null && t < fromT;
    if (f.op === 'after') return fromT !== null && t > fromT;
    if (f.op === 'on') {
      if (fromT === null) return true;
      const day = new Date(f.from!);
      const start = new Date(day.getFullYear(), day.getMonth(), day.getDate()).getTime();
      const end = start + 24 * 3600 * 1000;
      return t >= start && t < end;
    }
    // between
    if (fromT !== null && t < fromT) return false;
    if (toT !== null && t > toT) return false;
    return fromT !== null || toT !== null;
  }

  const filtered = $derived.by(() => {
    let rows = [...archivedExcluded];

    const q = filter.trim().toLowerCase();
    if (q) {
      rows = rows.filter(
        (p) =>
          p.pathWithNamespace.toLowerCase().includes(q) ||
          p.name.toLowerCase().includes(q) ||
          (p.description ?? '').toLowerCase().includes(q),
      );
    }

    rows = rows.filter(
      (p) =>
        matchSet(fStarred, p.starred ? 'starred' : 'unstarred') &&
        matchSet(fNamespace, namespaceOf(p.pathWithNamespace)) &&
        matchNumber(fBranches, p.branchCount) &&
        matchNumber(fOpenMrs, p.openMergeRequestCount) &&
        matchDate(fActivity, p.lastActivityAt),
    );

    if (sortCol) {
      const dir = sortDir === 'asc' ? 1 : -1;
      const cmp = (a: ProjectListItem, b: ProjectListItem) => {
        switch (sortCol) {
          case 'namespace':
            return namespaceOf(a.pathWithNamespace).localeCompare(namespaceOf(b.pathWithNamespace)) * dir;
          case 'project':
            return a.name.localeCompare(b.name) * dir;
          case 'branches':
            return (a.branchCount - b.branchCount) * dir;
          case 'openMrs':
            return (a.openMergeRequestCount - b.openMergeRequestCount) * dir;
          case 'activity': {
            const ta = a.lastActivityAt ? new Date(a.lastActivityAt).getTime() : 0;
            const tb = b.lastActivityAt ? new Date(b.lastActivityAt).getTime() : 0;
            return (ta - tb) * dir;
          }
          default:
            return 0;
        }
      };
      rows.sort(cmp);
    }

    return rows;
  });

  function open(p: ProjectListItem) {
    route.goProject(p.id);
  }

  const headerLabelStyle =
    'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';

  const editorialThStyle =
    'font-family:var(--font-mono); font-size:10.5px; font-weight:500; letter-spacing:.12em; color:var(--subink); text-transform:uppercase';

  const thStyle = $derived(chrome.editorialDataList ? editorialThStyle : headerLabelStyle);
</script>

<div class="flex min-h-0 flex-1" class:bg-bg={chrome.navSidebar}>
  {#if chrome.navSidebar}
    <NavRail />
  {/if}
  <div class="flex min-h-0 min-w-0 flex-1 flex-col" class:bg-bg={chrome.editorialListShell}>
    <PageHeader
      title={chrome.sectionLabelCasing === 'sentence' ? 'Projects' : 'PROJECTS'}
      headerClass={chrome.editorialListShell ? '!bg-bg !px-7 !pb-3.5 !pt-[18px] border-b-[0.5px]' : ''}
    >
      {#snippet right()}
        {#if chrome.editorialListShell}
          <label
            for="projects-filter-input"
            class="focus-ring-within flex w-[280px] max-w-full shrink-0 cursor-text items-center gap-2 bg-paper py-1.5 pr-3 pl-3 text-[12px] text-subink"
            style="border:0.5px solid var(--hair); border-radius:var(--radius)"
          >
            <span class="shrink-0 select-none" style="font-size:12px" aria-hidden="true">⌕</span>
            <input
              id="projects-filter-input"
              type="text"
              bind:value={filter}
              placeholder="Filter projects…"
              autocomplete="off"
              spellcheck="false"
              class="min-w-0 flex-1 border-0 bg-transparent p-0 text-ink outline-none placeholder:text-mute"
              style="font-family:var(--font-sans); font-size:12px"
            />
            <span class="ml-auto shrink-0 select-none font-mono text-[10px] text-subink" aria-hidden="true">/</span>
          </label>
        {:else}
          <input
            type="text"
            bind:value={filter}
            placeholder="filter…"
            class="focus-ring border border-hair-strong bg-paper px-3 py-1.5 text-ink placeholder:text-mute"
            style="font-family:var(--font-mono); font-size:12px; border-radius:var(--radius); width:220px"
          />
        {/if}
      {/snippet}
    </PageHeader>

    <div class="min-h-0 flex-1 overflow-auto bg-bg">
      {#if error}
        <p class="p-6 text-danger">{error}</p>
      {:else if loading}
        <p class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
      {:else}
        <table
          class="w-full border-collapse"
          class:bg-transparent={chrome.editorialDataList}
          class:bg-paper={!chrome.editorialDataList}
          style={chrome.editorialDataList ? 'font-size:12.5px' : undefined}
        >
          <thead class="projects-thead sticky top-0 z-10 bg-paper">
            <tr class="text-left">
              <TableHeaderFilter
                label="★"
                kind="set"
                options={starredOptions}
                sortable={false}
                sortDir={null}
                onSort={() => {}}
                filter={fStarred}
                onFilterChange={(f) => (fStarred = f)}
                thClass="{chrome.editorialDataList ? 'py-2.5 pl-7' : 'py-2 pl-6'} w-8"
                thStyle={thStyle}
              />
              <TableHeaderFilter
                label={chrome.editorialDataList ? 'Project' : 'PROJECT'}
                kind="text"
                filterable={false}
                sortDir={dirFor('project')}
                onSort={() => toggleSort('project')}
                filter={emptyFilter}
                onFilterChange={() => {}}
                thClass={chrome.editorialDataList ? 'py-2.5' : 'py-2'}
                thStyle={thStyle}
              />
              <TableHeaderFilter
                label={chrome.editorialDataList ? 'Namespace' : 'NAMESPACE'}
                kind="set"
                options={namespaceOptions}
                sortDir={dirFor('namespace')}
                onSort={() => toggleSort('namespace')}
                filter={fNamespace}
                onFilterChange={(f) => (fNamespace = f)}
                thClass={chrome.editorialDataList ? 'py-2.5' : 'py-2'}
                thStyle={thStyle}
              />
              <TableHeaderFilter
                label={chrome.editorialDataList ? 'Branches' : 'BRANCHES'}
                align="right"
                kind="number"
                sortDir={dirFor('branches')}
                onSort={() => toggleSort('branches')}
                filter={fBranches}
                onFilterChange={(f) => (fBranches = f)}
                thClass={chrome.editorialDataList ? 'py-2.5' : 'py-2'}
                thStyle={thStyle}
              />
              <TableHeaderFilter
                label={chrome.editorialDataList ? 'Open MRs' : 'OPEN MRS'}
                align="right"
                kind="number"
                sortDir={dirFor('openMrs')}
                onSort={() => toggleSort('openMrs')}
                filter={fOpenMrs}
                onFilterChange={(f) => (fOpenMrs = f)}
                thClass={chrome.editorialDataList ? 'py-2.5' : 'py-2'}
                thStyle={thStyle}
              />
              <TableHeaderFilter
                label={chrome.editorialDataList ? 'Activity' : 'ACTIVITY'}
                align="right"
                kind="date"
                sortDir={dirFor('activity')}
                onSort={() => toggleSort('activity')}
                filter={fActivity}
                onFilterChange={(f) => (fActivity = f)}
                thClass={chrome.editorialDataList ? 'py-2.5 pr-7' : 'py-2 pr-6'}
                thStyle={thStyle}
              />
            </tr>
          </thead>
          <tbody>
            {#if filtered.length === 0}
              <tr>
                <td colspan="6" class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">
                  no projects match
                </td>
              </tr>
            {:else}
              {#each filtered as p, rowIdx (p.id)}
                <tr
                  class="group cursor-pointer transition-colors hover:bg-panel-2"
                  class:border-b={!chrome.editorialDataList}
                  class:border-hair={!chrome.editorialDataList}
                  style={chrome.editorialDataList && rowIdx > 0 ? 'border-top:0.5px solid var(--hair)' : undefined}
                  onclick={() => open(p)}
                >
                  <td
                    class="text-mute"
                    class:py-2.5={!chrome.editorialDataList}
                    class:pl-6={!chrome.editorialDataList}
                    class:py-[11px]={chrome.editorialDataList}
                    class:pl-7={chrome.editorialDataList}
                  >
                    <button
                      type="button"
                      class="star-btn cursor-pointer"
                      class:star-on={p.starred}
                      aria-pressed={p.starred}
                      aria-label={p.starred ? 'Unstar project' : 'Star project'}
                      onclick={(e) => toggleStar(p, e)}
                    >★</button>
                  </td>
                  <td class:py-2.5={!chrome.editorialDataList} class:py-[11px]={chrome.editorialDataList}>
                    {#if chrome.editorialDataList}
                      <span class="block truncate font-mono text-[12.5px] text-ink" style="font-weight:500">{p.name}</span>
                    {:else}
                      <span class="block truncate text-ink" style="font-family:var(--font-mono); font-size:12.5px; font-weight:500">{p.name}</span>
                    {/if}
                    {#if p.description && !chrome.editorialDataList}
                      <div class="truncate text-mute" style="font-size:11.5px; max-width:560px">{p.description}</div>
                    {/if}
                  </td>
                  <td
                    class="max-w-[280px] truncate"
                    class:py-2.5={!chrome.editorialDataList}
                    class:py-[11px]={chrome.editorialDataList}
                  >
                    {#if chrome.editorialDataList}
                      <span class="block truncate font-mono text-[12.5px] text-subink">{namespaceOf(p.pathWithNamespace) || '—'}</span>
                    {:else}
                      <span class="block truncate text-mute" style="font-family:var(--font-mono); font-size:12px">{namespaceOf(p.pathWithNamespace) || '—'}</span>
                    {/if}
                  </td>
                  <td
                    class="py-2.5 text-right text-subink"
                    class:py-[11px]={chrome.editorialDataList}
                    style="font-family:var(--font-mono); font-size:11.5px"
                  >
                    {p.branchCount}
                  </td>
                  <td
                    class="py-2.5 text-right"
                    class:py-[11px]={chrome.editorialDataList}
                    style="font-family:var(--font-mono); font-size:11.5px"
                  >
                    {#if chrome.editorialDataList}
                      <span class:text-ink={p.openMergeRequestCount > 0} class:text-subink={p.openMergeRequestCount === 0}>
                        {p.openMergeRequestCount}
                      </span>
                    {:else}
                      <span class:text-accent={p.openMergeRequestCount > 0} class:text-mute={p.openMergeRequestCount === 0}>
                        {p.openMergeRequestCount}
                      </span>
                    {/if}
                  </td>
                  <td
                    class="py-2.5 text-right text-mute"
                    class:py-[11px]={chrome.editorialDataList}
                    class:pr-6={!chrome.editorialDataList}
                    class:pr-7={chrome.editorialDataList}
                    style="font-family:var(--font-mono); font-size:11px"
                  >
                    {timeAgo(p.lastActivityAt)}
                  </td>
                </tr>
              {/each}
            {/if}
          </tbody>
        </table>
      {/if}
    </div>
  </div>
</div>

<style>
  /* Keep a hairline border under the sticky header even while scrolling.
     Applied per-th so it renders correctly with border-collapse and stays
     visible above the scrolling tbody. */
  /* border-collapse:collapse drops borders on sticky cells when the body
     scrolls past, so use an inset box-shadow which is painted as part of
     the cell itself and stays visible. */
  .projects-thead :global(th) {
    box-shadow: inset 0 -1px 0 0 var(--hair);
    background-clip: padding-box;
  }

  /* Click-to-toggle favourite. Off state is a near-invisible glyph that becomes
     legible on row hover; on state lights up gold without affecting layout. */
  .star-btn {
    background: transparent;
    border: 0;
    /* Left padding is 0 so the body glyph sits at the same x as the header ★, which uses
       padding:0 inside its filter trigger. Right padding keeps a small click target buffer. */
    padding: 2px 4px 2px 0;
    font-size: 12px;
    line-height: 1;
    color: rgba(26, 21, 18, 0.15);
    transition: color 120ms;
  }
  tr:hover .star-btn {
    color: var(--subink);
  }
  .star-btn:hover {
    color: var(--gold, #c9a042);
  }
  .star-btn.star-on,
  tr:hover .star-btn.star-on {
    color: var(--gold, #c9a042);
  }
</style>
