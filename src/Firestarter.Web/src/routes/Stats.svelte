<script lang="ts">
  import { onMount } from 'svelte';
  import { api, type ApiCallEntry, type ApiCallSummary } from '../lib/api';
  import { theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import NavRail from '../lib/components/NavRail.svelte';
  import TableHeaderFilter, {
    type FilterState,
    type SortDir,
    isFilterActive,
  } from '../lib/components/TableHeaderFilter.svelte';

  type ColId = 'time' | 'method' | 'host' | 'path' | 'status' | 'duration' | 'source';

  let summary = $state<ApiCallSummary | null>(null);
  let entries = $state<ApiCallEntry[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  let sortCol = $state<ColId | null>('time');
  let sortDir = $state<'asc' | 'desc'>('desc');

  const pageSize = 50;
  let page = $state(0);

  const emptyFilter: FilterState = { kind: 'none' };
  let fTime = $state<FilterState>(emptyFilter);
  let fMethod = $state<FilterState>(emptyFilter);
  let fHost = $state<FilterState>(emptyFilter);
  let fStatus = $state<FilterState>(emptyFilter);
  let fDuration = $state<FilterState>(emptyFilter);
  let fSource = $state<FilterState>(emptyFilter);

  const chrome = $derived(theme.chrome);

  let didInitialLoad = $state(false);

  onMount(async () => {
    await load();
    didInitialLoad = true;
  });

  async function load() {
    loading = true;
    error = null;
    try {
      const [s, l] = await Promise.all([api.getStatsSummary(), fetchEntries()]);
      summary = s;
      entries = l.entries;
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      loading = false;
    }
  }

  function fetchEntries() {
    const range = dateRangeFromFilter(fTime);
    return api.listStats({
      take: range ? 5000 : 500,
      from: range?.from,
      to: range?.to,
    });
  }

  // Re-fetch entries from the server when the time filter changes so the table sees the full
  // matching range (otherwise busy days would be capped by the default 500-row limit).
  $effect(() => {
    void fTime;
    if (!didInitialLoad) return;
    fetchEntries()
      .then((r) => (entries = r.entries))
      .catch((err) => {
        error = err instanceof Error ? err.message : String(err);
      });
  });

  /** Translates the user's date filter into a UTC ISO range that matches how rows are aggregated server-side. */
  function dateRangeFromFilter(f: FilterState): { from?: string; to?: string } | null {
    if (f.kind !== 'date' || !isFilterActive(f)) return null;
    const dayUtc = (s: string, plusDays = 0) => {
      const d = new Date(s + 'T00:00:00Z');
      if (plusDays !== 0) d.setUTCDate(d.getUTCDate() + plusDays);
      return d.toISOString();
    };
    if (f.op === 'on' && f.from) return { from: dayUtc(f.from), to: dayUtc(f.from, 1) };
    if (f.op === 'before' && f.from) return { to: dayUtc(f.from) };
    if (f.op === 'after' && f.from) return { from: dayUtc(f.from, 1) };
    // between
    const r: { from?: string; to?: string } = {};
    if (f.from) r.from = dayUtc(f.from);
    if (f.to) r.to = dayUtc(f.to, 1);
    return r.from || r.to ? r : null;
  }

  function refresh() {
    load();
  }

  function fmtDuration(ms: number): string {
    if (ms < 1000) return `${ms} ms`;
    return `${(ms / 1000).toFixed(2)} s`;
  }

  function fmtTime(iso: string): string {
    try {
      const d = new Date(iso);
      const pad = (n: number) => n.toString().padStart(2, '0');
      const date = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
      const time = `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
      return `${date} ${time}`;
    } catch {
      return iso;
    }
  }

  function fmtDateLabel(date: string): string {
    const d = new Date(date + 'T00:00:00Z');
    return d.toLocaleDateString(undefined, { weekday: 'short', day: '2-digit' });
  }

  function statusClass(code: number): string {
    if (code >= 200 && code < 300) return 'text-ok';
    if (code >= 300 && code < 400) return 'text-warn';
    return 'text-danger';
  }

  // ---- chart geometry ----
  const chartHeight = 120;
  const sourceColors: Record<string, string> = {
    gitlab: 'var(--accent)',
    jenkins: '#6b8a64',
    git: '#8a6e4b',
    other: 'rgba(26,21,18,0.35)',
  };
  function colorFor(src: string): string {
    return sourceColors[src] ?? 'rgba(26,21,18,0.35)';
  }

  /**
   * Toggle the Time filter to "on the given day" when a day bar in the weekly chart is clicked.
   * Re-clicking the already-selected day clears the filter.
   */
  function selectDay(date: string) {
    if (fTime.kind === 'date' && fTime.op === 'on' && fTime.from === date) {
      fTime = emptyFilter;
    } else {
      fTime = { kind: 'date', op: 'on', from: date, to: null };
    }
  }

  function isDaySelected(date: string): boolean {
    return fTime.kind === 'date' && fTime.op === 'on' && fTime.from === date;
  }

  const chartMax = $derived.by(() => {
    if (!summary) return 1;
    return Math.max(1, ...summary.byDay.map((d) => d.total));
  });

  // ---- table filtering / sorting ----
  function dirFor(col: ColId): SortDir {
    return sortCol === col ? sortDir : null;
  }

  function toggleSort(col: ColId) {
    if (sortCol === col) {
      sortDir = sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      sortCol = col;
      sortDir = col === 'time' || col === 'duration' ? 'desc' : 'asc';
    }
  }

  function matchSet(f: FilterState, value: string): boolean {
    if (f.kind !== 'set' || !isFilterActive(f)) return true;
    return f.selected.includes(value);
  }

  function matchText(f: FilterState, value: string): boolean {
    if (f.kind !== 'text' || !isFilterActive(f)) return true;
    return value.toLowerCase().includes(f.value.trim().toLowerCase());
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
    // UTC boundaries — must agree with the server-side range computed in dateRangeFromFilter
    // so a row delivered by the API is not then filtered out client-side.
    const dayStart = (s: string, plusDays = 0) => {
      const d = new Date(s + 'T00:00:00Z');
      if (plusDays !== 0) d.setUTCDate(d.getUTCDate() + plusDays);
      return d.getTime();
    };
    if (f.op === 'before') return f.from ? t < dayStart(f.from) : true;
    if (f.op === 'after') return f.from ? t >= dayStart(f.from, 1) : true;
    if (f.op === 'on') {
      if (!f.from) return true;
      return t >= dayStart(f.from) && t < dayStart(f.from, 1);
    }
    // between
    if (f.from && t < dayStart(f.from)) return false;
    if (f.to && t >= dayStart(f.to, 1)) return false;
    return !!(f.from || f.to);
  }

  const methodOptions = $derived(Array.from(new Set(entries.map((e) => e.method))).sort());
  const sourceOptions = $derived(Array.from(new Set(entries.map((e) => e.source || 'other'))).sort());
  const statusOptions = $derived(
    Array.from(
      new Set(
        entries.map((e) => {
          if (e.statusCode === 0) return 'failed';
          if (e.statusCode >= 500) return '5xx';
          if (e.statusCode >= 400) return '4xx';
          if (e.statusCode >= 300) return '3xx';
          if (e.statusCode >= 200) return '2xx';
          return 'other';
        }),
      ),
    ).sort(),
  );
  function statusBucket(code: number): string {
    if (code === 0) return 'failed';
    if (code >= 500) return '5xx';
    if (code >= 400) return '4xx';
    if (code >= 300) return '3xx';
    if (code >= 200) return '2xx';
    return 'other';
  }

  // Reset to first page whenever filters or sort change.
  $effect(() => {
    void fTime;
    void fMethod;
    void fHost;
    void fStatus;
    void fDuration;
    void fSource;
    void sortCol;
    void sortDir;
    page = 0;
  });

  const filtered = $derived.by(() => {
    let rows = [...entries];
    rows = rows.filter(
      (e) =>
        matchDate(fTime, e.timestamp) &&
        matchSet(fMethod, e.method) &&
        matchText(fHost, e.host) &&
        matchSet(fStatus, statusBucket(e.statusCode)) &&
        matchNumber(fDuration, e.durationMs) &&
        matchSet(fSource, e.source || 'other'),
    );
    if (sortCol) {
      const dir = sortDir === 'asc' ? 1 : -1;
      rows.sort((a, b) => {
        switch (sortCol) {
          case 'time':
            return (new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()) * dir;
          case 'method':
            return a.method.localeCompare(b.method) * dir;
          case 'host':
            return a.host.localeCompare(b.host) * dir;
          case 'path':
            return a.path.localeCompare(b.path) * dir;
          case 'status':
            return (a.statusCode - b.statusCode) * dir;
          case 'duration':
            return (a.durationMs - b.durationMs) * dir;
          case 'source':
            return a.source.localeCompare(b.source) * dir;
          default:
            return 0;
        }
      });
    }
    return rows;
  });

  const pageCount = $derived(Math.max(1, Math.ceil(filtered.length / pageSize)));
  const safePage = $derived(Math.min(page, pageCount - 1));
  const paged = $derived(filtered.slice(safePage * pageSize, safePage * pageSize + pageSize));
  const rangeStart = $derived(filtered.length === 0 ? 0 : safePage * pageSize + 1);
  const rangeEnd = $derived(Math.min(filtered.length, safePage * pageSize + pageSize));

  function goFirst() { page = 0; }
  function goPrev() { if (safePage > 0) page = safePage - 1; }
  function goNext() { if (safePage < pageCount - 1) page = safePage + 1; }
  function goLast() { page = pageCount - 1; }

  const thStyle =
    'font-family:var(--font-mono); font-size:10.5px; font-weight:500; letter-spacing:.12em; color:var(--subink); text-transform:uppercase';
</script>

<div class="flex min-h-0 flex-1" class:bg-bg={chrome.navSidebar}>
  {#if chrome.navSidebar}
    <NavRail />
  {/if}
  <div class="flex min-h-0 min-w-0 flex-1 flex-col" class:bg-bg={chrome.editorialListShell}>
    <PageHeader
      title={chrome.sectionLabelCasing === 'sentence' ? 'Statistics' : 'STATISTICS'}
      headerClass={chrome.editorialListShell ? '!bg-bg !px-7 !pb-3.5 !pt-[18px] border-b-[0.5px]' : ''}
    >
      {#snippet right()}
        <button
          type="button"
          onclick={refresh}
          class="focus-ring border border-hair bg-paper px-3 py-1.5 text-[12px] text-ink transition-colors hover:bg-panel-2"
          style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono)"
        >Refresh</button>
      {/snippet}
    </PageHeader>

    <div class="min-h-0 flex-1 overflow-auto bg-bg" style="scrollbar-gutter:stable">
      {#if error}
        <p class="p-6 text-danger">{error}</p>
      {:else if loading && !summary}
        <p class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
      {:else if summary}
        <div class="flex flex-col gap-6 p-7">
          <!-- KPIs -->
          <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
            {#each [
              { label: 'Calls today', value: summary.totalToday.toLocaleString() },
              { label: 'Calls last 7d', value: summary.totalLast7d.toLocaleString() },
              {
                label: 'Errors 7d',
                value: summary.failuresLast7d.toLocaleString(),
                hint:
                  summary.totalLast7d > 0
                    ? `${((summary.failuresLast7d / summary.totalLast7d) * 100).toFixed(1)}%`
                    : '—',
              },
              { label: 'Avg duration 7d', value: `${summary.averageDurationMsLast7d.toFixed(0)} ms` },
            ] as kpi (kpi.label)}
              <div
                class="border border-hair bg-paper p-4"
                style="border-radius:var(--radius); border-width:0.5px"
              >
                <div
                  class="text-mute"
                  style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; text-transform:uppercase"
                >{kpi.label}</div>
                <div class="mt-1 flex items-baseline gap-2">
                  <div class="text-ink" style="font-family:var(--font-mono); font-size:22px; font-weight:500">{kpi.value}</div>
                  {#if 'hint' in kpi && kpi.hint}
                    <div class="text-subink" style="font-family:var(--font-mono); font-size:11px">{kpi.hint}</div>
                  {/if}
                </div>
              </div>
            {/each}
          </div>

          <!-- Stacked bar chart, last 7 days by source -->
          <div
            class="border border-hair bg-paper p-4"
            style="border-radius:var(--radius); border-width:0.5px"
          >
            <div class="mb-5 flex items-center justify-between">
              <div
                class="text-subink"
                style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; text-transform:uppercase"
              >Last 7 days</div>
              <div class="flex items-center gap-3">
                {#each summary.sources as src (src)}
                  <div class="flex items-center gap-1.5 text-[11px] text-subink" style="font-family:var(--font-mono)">
                    <span class="inline-block size-2.5" style="background:{colorFor(src)}; border-radius:1px"></span>
                    {src}
                  </div>
                {/each}
                {#if summary.sources.length === 0}
                  <span class="text-mute" style="font-family:var(--font-mono); font-size:11px">no data</span>
                {/if}
              </div>
            </div>
            <div class="flex items-end gap-2">
              {#each summary.byDay as day (day.date)}
                {@const hPx = (day.total / chartMax) * chartHeight}
                {@const selected = isDaySelected(day.date)}
                <button
                  type="button"
                  class="day-bar flex flex-1 cursor-pointer flex-col items-center gap-1.5"
                  class:day-bar-selected={selected}
                  title="Filter to {day.date} ({day.total} calls)"
                  onclick={() => selectDay(day.date)}
                >
                  <div class="text-subink" style="font-family:var(--font-mono); font-size:10.5px; line-height:1; min-height:11px">
                    {day.total > 0 ? day.total : ' '}
                  </div>
                  <div
                    class="flex w-full flex-col-reverse overflow-hidden"
                    style="height:{chartHeight}px; min-height:1px; gap:1px"
                  >
                    {#if day.total > 0}
                      {#each summary.sources as src (src)}
                        {@const c = day.countsBySource[src] ?? 0}
                        {#if c > 0}
                          <div
                            style="height:{(c / day.total) * hPx}px; background:{colorFor(src)}; min-height:1px"
                            title="{src}: {c}"
                          ></div>
                        {/if}
                      {/each}
                    {/if}
                  </div>
                  <div
                    class="text-subink"
                    class:text-ink={selected}
                    style="font-family:var(--font-mono); font-size:10.5px"
                  >
                    {fmtDateLabel(day.date)}
                  </div>
                </button>
              {/each}
            </div>
          </div>

          <!-- Detailed network table -->
          <div
            class="border border-hair bg-paper"
            style="border-radius:var(--radius); border-width:0.5px"
          >
            <table class="stats-table w-full border-collapse" style="font-size:12px">
                <thead class="stats-thead sticky top-0 z-10 bg-paper">
                  <tr class="text-left">
                    <TableHeaderFilter
                      label="Time"
                      kind="date"
                      sortDir={dirFor('time')}
                      onSort={() => toggleSort('time')}
                      filter={fTime}
                      onFilterChange={(f) => (fTime = f)}
                      thClass="py-2 pl-4 pr-3"
                      thStyle={thStyle}
                    />
                    <TableHeaderFilter
                      label="Method"
                      kind="set"
                      options={methodOptions}
                      sortDir={dirFor('method')}
                      onSort={() => toggleSort('method')}
                      filter={fMethod}
                      onFilterChange={(f) => (fMethod = f)}
                      thClass="py-2 px-3"
                      thStyle={thStyle}
                    />
                    <TableHeaderFilter
                      label="Host"
                      kind="text"
                      sortDir={dirFor('host')}
                      onSort={() => toggleSort('host')}
                      filter={fHost}
                      onFilterChange={(f) => (fHost = f)}
                      thClass="py-2 px-3"
                      thStyle={thStyle}
                    />
                    <TableHeaderFilter
                      label="Path"
                      kind="text"
                      filterable={false}
                      sortDir={dirFor('path')}
                      onSort={() => toggleSort('path')}
                      filter={emptyFilter}
                      onFilterChange={() => {}}
                      thClass="py-2 px-3"
                      thStyle={thStyle}
                    />
                    <TableHeaderFilter
                      label="Status"
                      align="right"
                      kind="set"
                      options={statusOptions}
                      sortDir={dirFor('status')}
                      onSort={() => toggleSort('status')}
                      filter={fStatus}
                      onFilterChange={(f) => (fStatus = f)}
                      thClass="py-2 px-3"
                      thStyle={thStyle}
                    />
                    <TableHeaderFilter
                      label="Duration"
                      align="right"
                      kind="number"
                      sortDir={dirFor('duration')}
                      onSort={() => toggleSort('duration')}
                      filter={fDuration}
                      onFilterChange={(f) => (fDuration = f)}
                      thClass="py-2 px-3"
                      thStyle={thStyle}
                    />
                    <TableHeaderFilter
                      label="Source"
                      kind="set"
                      options={sourceOptions}
                      sortDir={dirFor('source')}
                      onSort={() => toggleSort('source')}
                      filter={fSource}
                      onFilterChange={(f) => (fSource = f)}
                      thClass="py-2 pl-3 pr-4"
                      thStyle={thStyle}
                    />
                  </tr>
                </thead>
                <tbody>
                  {#if filtered.length === 0}
                    <tr>
                      <td colspan="7" class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">
                        no calls match
                      </td>
                    </tr>
                  {:else}
                    {#each paged as e (e.id)}
                      <tr
                        class="transition-colors hover:bg-panel-2"
                        style="border-top:0.5px solid var(--hair)"
                        title={e.errorMessage ?? ''}
                      >
                        <td class="py-1.5 pl-4 pr-3 text-subink" style="font-family:var(--font-mono); font-size:11.5px">
                          {fmtTime(e.timestamp)}
                        </td>
                        <td class="px-3 py-1.5 text-ink" style="font-family:var(--font-mono); font-size:11.5px; font-weight:500">
                          {e.method}
                        </td>
                        <td class="max-w-[260px] truncate px-3 py-1.5 text-subink" style="font-family:var(--font-mono); font-size:11.5px">
                          {e.host}
                        </td>
                        <td class="max-w-[420px] truncate px-3 py-1.5 text-ink" style="font-family:var(--font-mono); font-size:11.5px">
                          {e.path}
                        </td>
                        <td class="px-3 py-1.5 text-right {statusClass(e.statusCode)}" style="font-family:var(--font-mono); font-size:11.5px">
                          {e.statusCode === 0 ? 'ERR' : e.statusCode}
                        </td>
                        <td class="px-3 py-1.5 text-right text-subink" style="font-family:var(--font-mono); font-size:11.5px">
                          {fmtDuration(e.durationMs)}
                        </td>
                        <td class="py-1.5 pl-3 pr-4 text-subink" style="font-family:var(--font-mono); font-size:11.5px">
                          {e.source}
                        </td>
                      </tr>
                    {/each}
                  {/if}
                </tbody>
              </table>
            <div
              class="flex items-center justify-between gap-3 px-4 py-2"
              style="border-top:0.5px solid var(--hair)"
            >
              <div class="text-subink" style="font-family:var(--font-mono); font-size:11px">
                {#if filtered.length === 0}
                  0 of 0
                {:else}
                  {rangeStart}–{rangeEnd} of {filtered.length}
                {/if}
              </div>
              <div class="flex items-center gap-1">
                <button
                  type="button"
                  class="cursor-pointer border border-hair bg-paper px-2 py-1 text-ink transition-colors hover:bg-panel-2 disabled:cursor-not-allowed disabled:text-mute disabled:hover:bg-paper"
                  style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:11.5px"
                  disabled={safePage === 0}
                  onclick={goFirst}
                  aria-label="First page"
                >«</button>
                <button
                  type="button"
                  class="cursor-pointer border border-hair bg-paper px-2 py-1 text-ink transition-colors hover:bg-panel-2 disabled:cursor-not-allowed disabled:text-mute disabled:hover:bg-paper"
                  style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:11.5px"
                  disabled={safePage === 0}
                  onclick={goPrev}
                  aria-label="Previous page"
                >‹</button>
                <span class="px-2 text-subink" style="font-family:var(--font-mono); font-size:11.5px">
                  {safePage + 1} / {pageCount}
                </span>
                <button
                  type="button"
                  class="cursor-pointer border border-hair bg-paper px-2 py-1 text-ink transition-colors hover:bg-panel-2 disabled:cursor-not-allowed disabled:text-mute disabled:hover:bg-paper"
                  style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:11.5px"
                  disabled={safePage >= pageCount - 1}
                  onclick={goNext}
                  aria-label="Next page"
                >›</button>
                <button
                  type="button"
                  class="cursor-pointer border border-hair bg-paper px-2 py-1 text-ink transition-colors hover:bg-panel-2 disabled:cursor-not-allowed disabled:text-mute disabled:hover:bg-paper"
                  style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:11.5px"
                  disabled={safePage >= pageCount - 1}
                  onclick={goLast}
                  aria-label="Last page"
                >»</button>
              </div>
            </div>
          </div>
        </div>
      {/if}
    </div>
  </div>
</div>

<style>
  .stats-thead :global(th) {
    box-shadow: inset 0 -1px 0 0 var(--hair);
    background-clip: padding-box;
  }

  /* Day columns in the weekly chart act as click-to-filter buttons. */
  .day-bar {
    background: transparent;
    border: 0;
    padding: 8px 10px;
    border-radius: 3px;
    transition: background-color 120ms;
  }
  .day-bar:hover {
    background: var(--panel-2);
  }
  .day-bar-selected {
    background: var(--panel-2);
    outline: 0.5px solid var(--hair-strong);
    outline-offset: -0.5px;
  }
</style>
