<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../api';
  import { workspaceOpen } from '../stores/workspaceOpenContext.svelte';
  import { route } from '../stores/route.svelte';
  import { theme } from '../stores/theme.svelte';

  let menuOpen = $state(false);
  let actionError = $state<string | null>(null);

  let rootEl: HTMLDivElement | undefined = $state();

  const ctx = $derived(workspaceOpen.current);
  const visible = $derived(ctx !== null && route.current.name !== 'start');
  const chrome = $derived(theme.chrome);
  const fabPanelBorderWidth = $derived(chrome.warmTitleBar ? '0.5px' : '1px');

  function closeMenu() {
    menuOpen = false;
  }

  function toggleMenu() {
    if (!ctx) return;
    actionError = null;
    menuOpen = !menuOpen;
  }

  function onWindowClick(e: MouseEvent) {
    if (!menuOpen) return;
    const t = e.target;
    if (t instanceof Node && rootEl && !rootEl.contains(t)) closeMenu();
  }

  onMount(() => {
    const key = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeMenu();
    };
    window.addEventListener('click', onWindowClick);
    window.addEventListener('keydown', key);
    return () => {
      window.removeEventListener('click', onWindowClick);
      window.removeEventListener('keydown', key);
    };
  });

  async function doIde() {
    if (!ctx) return;
    actionError = null;
    closeMenu();
    try {
      await api.openProject(ctx.projectId, ctx.branch);
    } catch (e) {
      actionError = e instanceof Error ? e.message : String(e);
    }
  }

  async function doExplorer() {
    if (!ctx) return;
    actionError = null;
    closeMenu();
    try {
      await api.openProjectExplorer(ctx.projectId, ctx.branch);
    } catch (e) {
      actionError = e instanceof Error ? e.message : String(e);
    }
  }

  async function doTerminal() {
    if (!ctx) return;
    actionError = null;
    closeMenu();
    try {
      await api.openProjectTerminal(ctx.projectId, ctx.branch);
    } catch (e) {
      actionError = e instanceof Error ? e.message : String(e);
    }
  }

  const itemStyle =
    'w-full text-left rounded px-3 py-2 text-[12px] text-ink transition-colors hover:bg-panel-2 disabled:opacity-50';
  const panelClass = 'min-w-[11rem] border border-hair bg-paper py-1 shadow-md';
</script>

{#if visible}
  <div
    bind:this={rootEl}
    class="pointer-events-auto relative flex h-6 w-6 shrink-0 items-center justify-center"
  >
    {#if menuOpen || actionError}
      <div
        class="absolute right-0 bottom-full z-50 mb-1.5 flex flex-col items-end gap-1.5"
        style="max-width:min(18rem, calc(100vw - 2rem))"
      >
        {#if menuOpen}
          <div class={panelClass} style="border-radius:var(--radius); border-width:{fabPanelBorderWidth}">
            <button
              type="button"
              role="menuitem"
              class={itemStyle}
              style="font-family:var(--font-sans)"
              onclick={doIde}
            >
              Open in IDE
            </button>
            <button
              type="button"
              role="menuitem"
              class={itemStyle}
              style="font-family:var(--font-sans)"
              onclick={doExplorer}
            >
              Open in Explorer
            </button>
            <button
              type="button"
              role="menuitem"
              class={itemStyle}
              style="font-family:var(--font-sans)"
              onclick={doTerminal}
            >
              Open in Terminal
            </button>
          </div>
        {/if}
        {#if actionError}
          <p
            class="max-w-[14rem] rounded border border-danger/40 bg-paper px-2 py-1 text-[11px] text-danger"
            style="font-family:var(--font-mono)"
          >
            {actionError}
          </p>
        {/if}
      </div>
    {/if}
    <button
      type="button"
      class="flex h-6 w-6 items-center justify-center rounded-md border text-subink transition-[color,background-color,box-shadow] hover:border-accent/40 hover:text-accent focus:outline-none focus-visible:ring-2 focus-visible:ring-accent/40 disabled:opacity-50"
      style="box-shadow:var(--shadow-sm); background:var(--panel-2); border-color:var(--hair); border-width:{fabPanelBorderWidth}"
      aria-label="Open workspace"
      aria-haspopup="menu"
      aria-expanded={menuOpen}
      disabled={!ctx}
      onclick={(e) => {
        e.stopPropagation();
        toggleMenu();
      }}
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
        <polyline points="16 18 22 12 16 6" />
        <polyline points="8 6 2 12 8 18" />
      </svg>
    </button>
  </div>
{/if}
