<script lang="ts">
  import { api, type ProjectSearchHit } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { workspaceOpen } from '../lib/stores/workspaceOpenContext.svelte';

  let query = $state('');
  let hits = $state<ProjectSearchHit[]>([]);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let selectedIndex = $state(0);

  let searchToken = 0;
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  const selected = $derived(hits[selectedIndex]);

  function openSelected() {
    if (!selected) return;
    route.goProject(selected.id);
  }

  $effect(() => {
    if (selected) {
      workspaceOpen.set({ projectId: selected.id, branch: selected.defaultBranch ?? null });
    } else {
      workspaceOpen.set(null);
    }
    return () => workspaceOpen.set(null);
  });

  async function runSearch(q: string) {
    const token = ++searchToken;
    loading = true;
    error = null;
    try {
      const result = await api.searchProjects(q, 25);
      if (token !== searchToken) return;
      hits = result.hits;
      selectedIndex = 0;
    } catch (err) {
      if (token !== searchToken) return;
      error = err instanceof Error ? err.message : String(err);
      hits = [];
    } finally {
      if (token === searchToken) loading = false;
    }
  }

  $effect(() => {
    const q = query;
    if (debounceTimer) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => runSearch(q), 120);
  });

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === 'Enter') {
      event.preventDefault();
      openSelected();
      return;
    }
    if (hits.length === 0) return;
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      selectedIndex = (selectedIndex + 1) % hits.length;
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      selectedIndex = (selectedIndex - 1 + hits.length) % hits.length;
    }
  }

  function formatTime(iso: string | null) {
    if (!iso) return '';
    const d = new Date(iso);
    return d.toLocaleDateString();
  }
</script>

<div class="mx-auto flex w-full max-w-3xl flex-col gap-4 px-6 pt-16">
  <h1 class="text-center text-2xl font-semibold tracking-tight">Find a project</h1>

  <input
    type="text"
    bind:value={query}
    onkeydown={onKeyDown}
    placeholder="Search projects…"
    autocomplete="off"
    spellcheck="false"
    class="w-full rounded-xl border border-zinc-700 bg-zinc-900 px-5 py-4 text-lg text-zinc-100 placeholder-zinc-500 outline-none transition focus:border-violet-500 focus:ring-2 focus:ring-violet-500/30"
  />

  {#if error}
    <p class="text-center text-sm text-rose-300">{error}</p>
  {:else if loading && hits.length === 0}
    <p class="text-center text-sm text-zinc-500">Searching…</p>
  {:else if hits.length === 0 && query.trim().length > 0}
    <p class="text-center text-sm text-zinc-500">No matches.</p>
  {:else if hits.length === 0}
    <p class="text-center text-sm text-zinc-500">
      Cached projects appear here. Run a sync from the Sync tab first.
    </p>
  {:else}
    <ul class="divide-y divide-zinc-800 overflow-hidden rounded-xl border border-zinc-800 bg-zinc-950/40">
      {#each hits as hit, i (hit.id)}
        <li>
          <button
            type="button"
            class="flex w-full items-center gap-4 px-4 py-3 text-left transition {i === selectedIndex
              ? 'bg-zinc-800/80'
              : 'hover:bg-zinc-900/70'}"
            onclick={() => (selectedIndex = i)}
          >
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-zinc-100">{hit.pathWithNamespace}</div>
              {#if hit.description}
                <div class="truncate text-xs text-zinc-500">{hit.description}</div>
              {/if}
            </div>
            <div class="shrink-0 text-right text-xs text-zinc-500">
              {#if hit.defaultBranch}
                <div class="font-mono text-zinc-400">{hit.defaultBranch}</div>
              {/if}
              {#if hit.lastActivityAt}
                <div>{formatTime(hit.lastActivityAt)}</div>
              {/if}
            </div>
          </button>
        </li>
      {/each}
    </ul>

    {#if selected}
      <div class="rounded-xl border border-zinc-800 bg-zinc-950/60 px-4 py-3">
        <div class="min-w-0">
          <div class="text-xs uppercase tracking-wider text-zinc-500">Selected</div>
          <div class="truncate text-sm text-zinc-200">{selected.pathWithNamespace}</div>
          {#if selected.defaultBranch}
            <div class="truncate font-mono text-xs text-zinc-500">branch: {selected.defaultBranch}</div>
          {/if}
        </div>
        <p class="mt-2 text-xs text-zinc-500">↵ opens project. Use the round button (main app) for IDE, Explorer, or Terminal.</p>
      </div>
    {/if}
  {/if}
</div>
