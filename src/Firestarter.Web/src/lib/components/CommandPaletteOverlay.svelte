<script lang="ts">
  import { api, type ProjectSearchHit } from '../api';
  import { route } from '../stores/route.svelte';

  interface Props {
    open: boolean;
  }
  let { open = $bindable(false) }: Props = $props();

  let query = $state('');
  let hits = $state<ProjectSearchHit[]>([]);
  let selectedIndex = $state(0);
  let loading = $state(false);
  let inputEl = $state<HTMLInputElement | null>(null);

  let searchToken = 0;
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;

  const selected = $derived(hits[selectedIndex]);
  const isSearching = $derived(query.trim().length > 0);
  const listTitle = $derived(isSearching ? 'RESULTS' : 'RECENT');

  async function runSearch(q: string) {
    const token = ++searchToken;
    loading = true;
    try {
      const res = await api.searchProjects(q, 6);
      if (token !== searchToken) return;
      hits = res.hits;
      selectedIndex = 0;
    } finally {
      if (token === searchToken) loading = false;
    }
  }

  // Open: reset state, run an initial empty search to fetch "recent", and focus the input.
  $effect(() => {
    if (!open) return;
    query = '';
    hits = [];
    selectedIndex = 0;
    void runSearch('');
    setTimeout(() => inputEl?.focus(), 0);
  });

  // Debounced search as the user types — only while open.
  $effect(() => {
    if (!open) return;
    const q = query;
    if (debounceTimer) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => runSearch(q), 120);
  });

  // Keyboard navigation while the overlay is open.
  $effect(() => {
    if (!open) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        open = false;
        return;
      }
      if (e.key === 'ArrowDown') {
        if (hits.length === 0) return;
        e.preventDefault();
        selectedIndex = (selectedIndex + 1) % hits.length;
      } else if (e.key === 'ArrowUp') {
        if (hits.length === 0) return;
        e.preventDefault();
        selectedIndex = (selectedIndex - 1 + hits.length) % hits.length;
      } else if (e.key === 'Enter') {
        if (!selected) return;
        e.preventDefault();
        openSelected();
      }
    }
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  });

  function openSelected() {
    if (!selected) return;
    route.goProject(selected.id);
    open = false;
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
    const m = s / 60;
    if (m < 60) return `${Math.round(m)}m ago`;
    const h = m / 60;
    if (h < 24) return `${Math.round(h)}h ago`;
    const d = h / 24;
    if (d < 30) return `${Math.round(d)}d ago`;
    const mo = d / 30;
    if (mo < 12) return `${Math.round(mo)}mo ago`;
    return `${Math.round(d / 365)}y ago`;
  }

  function onBackdropPointerDown(e: PointerEvent) {
    if (e.currentTarget !== e.target) return;
    open = false;
  }
</script>

{#if open}
  <div
    class="palette-backdrop"
    role="presentation"
    onpointerdown={onBackdropPointerDown}
  >
    <div
      class="palette-card"
      role="dialog"
      tabindex="-1"
      aria-label="Find a project or merge request"
      onpointerdown={(e) => e.stopPropagation()}
    >
      <div class="palette-input-wrap">
        <span class="palette-icon" aria-hidden="true">⌕</span>
        <input
          bind:this={inputEl}
          bind:value={query}
          type="text"
          placeholder="Find a project or merge request"
          autocomplete="off"
          spellcheck="false"
          class="palette-input"
        />
        <span class="palette-hint" aria-hidden="true">Esc</span>
      </div>

      {#if hits.length > 0}
        <div class="palette-list-header">{listTitle}</div>
        <ul class="palette-list">
          {#each hits as hit, i (hit.id)}
            {@const parts = splitNs(hit.pathWithNamespace)}
            {@const active = i === selectedIndex}
            {@const age = ageOf(hit.lastActivityAt)}
            <li>
              <button
                type="button"
                tabindex="-1"
                class="palette-row"
                class:palette-row-active={active}
                onmouseenter={() => (selectedIndex = i)}
                onclick={() => {
                  selectedIndex = i;
                  openSelected();
                }}
              >
                <span class="palette-badge">{firstLetter(hit.pathWithNamespace)}</span>
                <span class="palette-path">
                  <span class="text-subink">{parts.ns}</span><span style="font-weight:500">{parts.name}</span>
                </span>
                <span class="palette-meta">
                  {#if hit.defaultBranch}{hit.defaultBranch}{/if}{#if hit.defaultBranch && age} · {/if}{#if age}{age}{/if}
                </span>
                {#if active}
                  <span class="palette-enter" aria-hidden="true">↵</span>
                {/if}
              </button>
            </li>
          {/each}
        </ul>
      {:else if !loading}
        <p class="palette-empty">No matches.</p>
      {/if}

      {#if selected}
        <div class="palette-selected">
          <span class="text-mute">selected:</span>
          <span style="font-family:var(--font-mono); margin-left:8px; color:var(--ink)">{selected.pathWithNamespace}</span>
        </div>
      {/if}

      <div class="palette-footer">
        <span>↑↓ navigate · ↵ open · Esc close</span>
      </div>
    </div>
  </div>
{/if}

<style>
  .palette-backdrop {
    position: fixed;
    inset: 0;
    z-index: 100;
    background: rgba(26, 21, 18, 0.32);
    backdrop-filter: blur(2px);
    display: flex;
    align-items: flex-start;
    justify-content: center;
    padding: 12vh 1rem 1rem;
  }
  .palette-card {
    width: min(680px, 100%);
    background: var(--paper);
    border: 0.5px solid var(--hair-strong);
    border-radius: 8px;
    box-shadow: var(--shadow);
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }
  .palette-input-wrap {
    position: relative;
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 14px 14px 14px 38px;
    border-bottom: 0.5px solid var(--hair);
  }
  .palette-icon {
    position: absolute;
    left: 14px;
    color: var(--mute);
    font-size: 14px;
  }
  .palette-input {
    flex: 1;
    border: 0;
    background: transparent;
    font-family: var(--font-mono);
    font-size: 13.5px;
    color: var(--ink);
    outline: none;
  }
  .palette-input::placeholder { color: var(--mute); }
  .palette-hint {
    border: 0.5px solid var(--hair-strong);
    background: var(--panel-2);
    color: var(--mute);
    padding: 2px 6px;
    border-radius: 3px;
    font-family: var(--font-mono);
    font-size: 10.5px;
  }
  .palette-list-header {
    padding: 10px 14px 4px;
    color: var(--subink);
    font-size: 10px;
    font-weight: 500;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    font-family: var(--font-sans);
  }
  .palette-list {
    list-style: none;
    margin: 0;
    padding: 0 0 6px;
  }
  .palette-row {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 8px 14px;
    background: transparent;
    border: 0;
    border-left: 2px solid transparent;
    cursor: pointer;
    text-align: left;
    transition: background 100ms;
  }
  .palette-row-active {
    background: rgba(201, 71, 31, 0.06);
    border-left-color: var(--accent);
  }
  .palette-badge {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 22px;
    height: 22px;
    border-radius: 4px;
    background: var(--accent-soft);
    color: var(--accent);
    font-family: var(--font-mono);
    font-size: 10px;
    font-weight: 600;
    flex-shrink: 0;
  }
  .palette-row-active .palette-badge {
    background: var(--accent);
    color: var(--paper);
  }
  .palette-path {
    flex: 1;
    min-width: 0;
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--ink);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .palette-meta {
    flex-shrink: 0;
    color: var(--mute);
    font-family: var(--font-mono);
    font-size: 10.5px;
  }
  .palette-enter {
    flex-shrink: 0;
    border: 0.5px solid var(--hair);
    border-radius: 3px;
    padding: 1px 5px;
    color: var(--subink);
    font-family: var(--font-mono);
    font-size: 10px;
  }
  .palette-empty {
    padding: 14px;
    color: var(--mute);
    font-size: 12px;
    text-align: center;
  }
  .palette-selected {
    padding: 10px 14px;
    border-top: 0.5px solid var(--hair);
    font-size: 12px;
    color: var(--subink);
  }
  .palette-footer {
    padding: 8px 14px;
    border-top: 0.5px solid var(--hair);
    color: var(--mute);
    font-family: var(--font-mono);
    font-size: 10.5px;
    letter-spacing: 0.04em;
  }
</style>
