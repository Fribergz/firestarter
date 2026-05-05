<script lang="ts">
  import { onMount } from 'svelte';
  import { api, type ProjectSearchHit, type CountersDto } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import { kpiNumeralFontWeightCss, startHeroInlineStyle } from '../lib/theme/chrome';

  let query = $state('');
  let hits = $state<ProjectSearchHit[]>([]);
  let selectedIndex = $state(0);
  let loading = $state(false);
  let counters = $state<CountersDto>({
    authoredOrAssignedOpen: 0,
    reviewerOpen: 0,
    projects: 0,
    branches: 0,
  });

  let searchToken = 0;
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  const selected = $derived(hits[selectedIndex]);
  const isSearching = $derived(query.trim().length > 0);
  const listTitle = $derived(isSearching ? 'RESULTS' : 'RECENT');

  type Tile = { label: string; sub: string; value: number; accent: boolean; onClick?: () => void };
  const tiles = $derived<Tile[]>([
    { label: 'authored', sub: 'merge requests open', value: counters.authoredOrAssignedOpen, accent: false, onClick: () => route.goMrMine() },
    { label: 'reviewing', sub: 'awaiting your approval', value: counters.reviewerOpen, accent: true, onClick: () => route.goMrReviewer() },
    { label: 'projects', sub: 'cached locally', value: counters.projects, accent: false, onClick: () => route.goProjects() },
    { label: 'branches', sub: 'indexed', value: counters.branches, accent: false },
  ]);

  onMount(async () => {
    try {
      counters = await api.getCounters();
    } catch {}
    void runSearch('');
  });

  async function runSearch(q: string) {
    const token = ++searchToken;
    loading = true;
    try {
      const result = await api.searchProjects(q, 6);
      if (token !== searchToken) return;
      hits = result.hits;
      selectedIndex = 0;
    } finally {
      if (token === searchToken) loading = false;
    }
  }

  $effect(() => {
    const q = query;
    if (debounceTimer) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => runSearch(q), 120);
  });

  $effect(() => {
    function onGlobalKey(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null;
      if (event.key === 'ArrowDown') {
        if (hits.length === 0) return;
        event.preventDefault();
        selectedIndex = (selectedIndex + 1) % hits.length;
      } else if (event.key === 'ArrowUp') {
        if (hits.length === 0) return;
        event.preventDefault();
        selectedIndex = (selectedIndex - 1 + hits.length) % hits.length;
      } else if (event.key === 'Enter') {
        if (target?.tagName === 'BUTTON') return;
        if (!selected) return;
        event.preventDefault();
        openSelected();
      }
    }
    window.addEventListener('keydown', onGlobalKey);
    return () => window.removeEventListener('keydown', onGlobalKey);
  });

  async function openSelected() {
    if (!selected) return;
    route.goProject(selected.id);
  }

  function splitNs(path: string) {
    const i = path.lastIndexOf('/');
    return i < 0 ? { ns: '', name: path } : { ns: path.slice(0, i + 1), name: path.slice(i + 1) };
  }

  function firstLetter(path: string) {
    const last = path.lastIndexOf('/');
    const base = last < 0 ? path : path.slice(last + 1);
    return (base[0] ?? '?').toUpperCase();
  }

  function ageOf(iso: string | null) {
    if (!iso) return null;
    const then = Date.parse(iso);
    if (Number.isNaN(then)) return null;
    const s = Math.max(0, (Date.now() - then) / 1000);
    if (s < 60) return `${Math.round(s)}s ago`;
    const m = s / 60; if (m < 60) return `${Math.round(m)}m ago`;
    const h = m / 60; if (h < 24) return `${Math.round(h)}h ago`;
    const d = h / 24; if (d < 30) return `${Math.round(d)}d ago`;
    const mo = d / 30; if (mo < 12) return `${Math.round(mo)}mo ago`;
    return `${Math.round(d / 365)}y ago`;
  }

  const hintKey = $derived(navigator.platform.toLowerCase().includes('mac') ? '⌘F' : 'Ctrl+F');

  const chrome = $derived(theme.chrome);
  const heroStyle = $derived(startHeroInlineStyle(chrome));
  const kpiNumWeight = $derived(kpiNumeralFontWeightCss(chrome));

  const asciiLogo = `    ┌──────────────┐
    │  firestarter │
    └──────────────┘`;
</script>

{#if chrome.startHeroTone === 'editorial'}
  <section class="flex min-h-0 flex-1 flex-col overflow-auto">
    <!-- Counter strip — variant-ember-start.jsx -->
    <div class="flex justify-center gap-12 px-6 pt-14 pb-7">
      {#each tiles as tile, i (tile.label)}
        {#if i > 0}
          <div
            class="self-stretch bg-hair"
            style="width:0.5px; min-height:3.5rem; margin-top:0.5rem; margin-bottom:0.5rem"
            aria-hidden="true"
          ></div>
        {/if}
        {#if tile.onClick}
          <button
            type="button"
            tabindex="-1"
            onclick={tile.onClick}
            class="flex min-w-[120px] flex-col items-center text-center transition-opacity hover:opacity-80"
          >
            <div
              style="font-family:var(--font-serif); font-size:44px; font-weight:400; line-height:1; letter-spacing:-0.03em; color:{tile.accent ? 'var(--accent)' : 'var(--ink)'}"
            >
              {tile.value}
            </div>
            <div
              class="mt-1.5 text-ink"
              style="font-family:var(--font-sans); font-size:11px; font-weight:500; letter-spacing:0.12em; text-transform:uppercase"
            >
              {tile.label}
            </div>
            <div class="mt-0.5 text-subink" style="font-size:11px">{tile.sub}</div>
          </button>
        {:else}
          <div class="flex min-w-[120px] flex-col items-center text-center">
            <div
              style="font-family:var(--font-serif); font-size:44px; font-weight:400; line-height:1; letter-spacing:-0.03em; color:{tile.accent ? 'var(--accent)' : 'var(--ink)'}"
            >
              {tile.value}
            </div>
            <div
              class="mt-1.5 text-ink"
              style="font-family:var(--font-sans); font-size:11px; font-weight:500; letter-spacing:0.12em; text-transform:uppercase"
            >
              {tile.label}
            </div>
            <div class="mt-0.5 text-subink" style="font-size:11px">{tile.sub}</div>
          </div>
        {/if}
      {/each}
    </div>

    <!-- Hero + search — editorial stack (aligned upward; avoid vertical center drift) -->
    <div class="flex flex-1 flex-col items-center justify-start px-6 pb-16 pt-[calc(2rem+50px)] sm:px-16">
      <h1 class="mx-auto mb-2 max-w-[720px] text-center text-ink" style={heroStyle}>
        What are we <span style="font-family:var(--font-serif); font-style:italic; color:var(--accent); font-weight:400">shipping</span> today?
      </h1>
      <p class="mb-8 text-center text-subink" style="font-size:13px; letter-spacing:0.01em">
        Search by project, namespace, or paste a repo URL.
      </p>

      <div class="w-full max-w-[680px]">
        <div class="relative">
          <span class="pointer-events-none absolute top-1/2 left-4 -translate-y-1/2 text-mute" style="font-size:14px">⌕</span>
          <input
            id="start-find-input"
            type="text"
            bind:value={query}
            placeholder="Find a project or merge request"
            autocomplete="off"
            spellcheck="false"
            class="focus-ring w-full border border-hair-strong bg-paper pr-16 pl-10 py-3.5 text-ink placeholder:text-mute outline-none"
            style="font-family:var(--font-mono); font-size:13.5px; border-radius:var(--radius-lg)"
          />
          <span
            class="absolute top-1/2 right-3 -translate-y-1/2 border border-hair-strong bg-panel-2 px-1.5 py-0.5 text-mute"
            style="font-family:var(--font-mono); font-size:10.5px; border-radius:var(--radius-sm)"
          >
            {hintKey}
          </span>
        </div>

        {#if hits.length > 0}
          <div
            class="mt-1 w-full overflow-hidden border border-hair-strong bg-paper"
            style="border-radius:8px; box-shadow:var(--shadow)"
          >
            <div
              class="px-3.5 pb-1 pt-2.5 text-[10px] font-medium uppercase tracking-[0.14em] text-subink"
              style="font-family:var(--font-sans)"
            >
              {listTitle}
            </div>
            <ul>
              {#each hits as hit, i (hit.id)}
                {@const parts = splitNs(hit.pathWithNamespace)}
                {@const active = i === selectedIndex}
                {@const age = ageOf(hit.lastActivityAt)}
                <li>
                  <button
                    type="button"
                    tabindex="-1"
                    onclick={() => { selectedIndex = i; openSelected(); }}
                    onmouseenter={() => { selectedIndex = i; }}
                    class="flex w-full items-center gap-3 border-l-2 border-l-transparent px-3.5 py-2 text-left transition-colors"
                    style={active
                      ? 'background:rgba(201,71,31,0.06); border-left-color:var(--accent)'
                      : ''}
                  >
                    <span
                      class="flex size-[22px] shrink-0 items-center justify-center rounded-[4px] font-mono text-[10px] font-semibold"
                      style={active
                        ? 'background:var(--accent); color:var(--paper)'
                        : 'background:var(--accent-soft); color:var(--accent)'}
                    >{firstLetter(hit.pathWithNamespace)}</span>
                    <span class="min-w-0 flex-1">
                      <span class="block truncate font-mono text-xs text-ink">
                        <span class="text-subink">{parts.ns}</span><span style="font-weight:500">{parts.name}</span>
                      </span>
                      <span class="block truncate text-[11px] text-subink">
                        {#if hit.defaultBranch}{hit.defaultBranch}{/if}{#if hit.defaultBranch && age} · {/if}{#if age}{age}{/if}
                      </span>
                    </span>
                    {#if active}
                      <span
                        class="shrink-0 rounded-[3px] border border-hair px-[5px] py-0.5 font-mono text-[10px] text-subink"
                      >↵</span>
                    {/if}
                  </button>
                </li>
              {/each}
            </ul>
            <div
              class="flex items-center justify-between border-t border-hair px-3.5 py-2 text-[11px] text-subink"
              style="font-family:var(--font-mono)"
            >
              <span>↑↓ navigate · ↵ open · {hintKey} anywhere</span>
              <span>{counters.projects} projects</span>
            </div>
          </div>
        {:else if !loading}
          <p class="mt-4 text-center text-mute" style="font-size:12px">
            No cached projects yet. Run a sync from the Sync tab.
          </p>
        {/if}

        {#if selected}
          <div
            class="mt-3 border border-hair-strong bg-paper px-3.5 py-2.5"
            style="border-radius:var(--radius-lg); box-shadow:var(--shadow-sm)"
          >
            <div class="min-w-0 text-[12px] text-subink">
              <span class="text-mute">selected:</span>
              <span class="ml-2 text-ink" style="font-family:var(--font-mono)">{selected.pathWithNamespace}</span>
            </div>
            <p class="mt-1.5 text-mute" style="font-size:11px">↵ opens the project; IDE, Explorer, and Terminal are on the workspace button there.</p>
          </div>
        {/if}
      </div>
    </div>
  </section>
{:else}
  <section class="flex flex-1 flex-col overflow-auto">
    <div class="mx-auto flex w-full max-w-[720px] flex-1 flex-col items-center justify-center px-6 py-12">
      {#if chrome.startHeroTone === 'terminal'}
        <pre class="mb-6 text-accent" style="font-family:var(--font-mono); font-size:13px; line-height:1.3">{asciiLogo}</pre>
      {/if}

      <h1 class="mb-1 text-center text-ink" style={heroStyle}>
        {#if chrome.startHeroTone === 'industrial'}
          FIND · OPEN · SHIP
        {:else}
          $ firestarter --open
        {/if}
      </h1>
      <p class="mb-8 text-center text-subink" style="font-size:12.5px">
        {chrome.startHeroTone === 'industrial'
          ? 'SEARCH · MERGE REQUESTS · PIPELINES · EXTENSIONS'
          : 'search projects · open merge requests · run extensions'}
      </p>

      <div class="w-full" style="max-width:620px">
        <div class="relative">
          <input
            id="start-find-input"
            type="text"
            bind:value={query}
            placeholder="find…"
            autocomplete="off"
            spellcheck="false"
            class="focus-ring w-full border border-hair-strong bg-paper px-5 py-4 text-ink placeholder:text-mute outline-none"
            style="font-family:var(--font-mono); font-size:14px; border-radius:var(--radius-lg)"
          />
          <span
            class="absolute top-1/2 right-4 -translate-y-1/2 rounded border border-hair-strong bg-panel-2 px-1.5 py-0.5 text-mute"
            style="font-family:var(--font-mono); font-size:10.5px; border-radius:var(--radius-sm)"
          >
            {hintKey}
          </span>
        </div>

        {#if hits.length > 0}
          <div class="mt-3 overflow-hidden border border-hair bg-paper" style="border-radius:var(--radius-lg)">
            <div
              class="flex items-center justify-between border-b border-hair px-4 py-2 text-mute"
              style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.08em"
            >
              <span>{listTitle} · {hits.length}</span>
              <span>[↑][↓] NAVIGATE · [↵] OPEN</span>
            </div>
            <ul>
              {#each hits as hit, i (hit.id)}
                {@const parts = splitNs(hit.pathWithNamespace)}
                {@const active = i === selectedIndex}
                <li>
                  <button
                    type="button"
                    tabindex="-1"
                    onclick={() => { selectedIndex = i; openSelected(); }}
                    class="relative flex w-full items-center gap-3 px-4 py-2.5 text-left"
                    style={active ? 'background:var(--accent-soft);' : ''}
                  >
                    {#if active}
                      <span
                        class="absolute inset-y-0 left-0"
                        style="background:var(--accent); width:{chrome.startHeroTone === 'terminal' ? '2px' : '3px'}"
                      ></span>
                    {/if}
                    <span class="min-w-0 flex-1 truncate">
                      <span class="text-mute" style="font-family:var(--font-mono); font-size:12px">{parts.ns}</span>
                      <span class="text-ink" style="font-family:var(--font-mono); font-size:12.5px; font-weight:500">{parts.name}</span>
                    </span>
                    {#if hit.defaultBranch}
                      <span class="shrink-0 text-mute" style="font-family:var(--font-mono); font-size:11px">{hit.defaultBranch}</span>
                    {/if}
                    {#if active}
                      <span class="shrink-0 text-accent" style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.08em">
                        [↵] OPEN
                      </span>
                    {/if}
                  </button>
                </li>
              {/each}
            </ul>
          </div>
        {:else if !loading}
          <p class="mt-4 text-center text-mute" style="font-size:12px">
            No cached projects yet. Run a sync from the Sync tab.
          </p>
        {/if}

        {#if selected}
          <div class="mt-3 border border-hair bg-paper px-4 py-2.5" style="border-radius:var(--radius-lg)">
            <div class="min-w-0 text-[12px] text-subink">
              <span class="text-mute">selected:</span>
              <span class="ml-2 text-ink" style="font-family:var(--font-mono)">{selected.pathWithNamespace}</span>
            </div>
            <p class="mt-1.5 text-mute" style="font-size:11px">↵ opens the project; IDE, Explorer, and Terminal are on the workspace button there.</p>
          </div>
        {/if}
      </div>
    </div>

    <div class="grid grid-cols-4 gap-[1px] border-t border-hair bg-hair">
      {#each tiles as tile (tile.label)}
        {#if tile.onClick}
          <button
            type="button"
            tabindex="-1"
            onclick={tile.onClick}
            class="flex flex-col gap-1 bg-paper px-5 py-4 text-left transition-colors hover:bg-panel-2"
          >
            <div
              class="text-mute"
              style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em"
            >
              {tile.label.toUpperCase()}
            </div>
            <div
              style="font-family:var(--font-mono); font-size:28px; font-weight:{kpiNumWeight}; color:{tile.accent ? 'var(--accent)' : 'var(--ink)'}"
            >
              {tile.value}
            </div>
          </button>
        {:else}
          <div class="flex flex-col gap-1 bg-paper px-5 py-4">
            <div
              class="text-mute"
              style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em"
            >
              {tile.label.toUpperCase()}
            </div>
            <div
              style="font-family:var(--font-mono); font-size:28px; font-weight:{kpiNumWeight}; color:{tile.accent ? 'var(--accent)' : 'var(--ink)'}"
            >
              {tile.value}
            </div>
          </div>
        {/if}
      {/each}
    </div>
  </section>
{/if}
