<script lang="ts">
  import { theme } from '../stores/theme.svelte';
  import { route } from '../stores/route.svelte';
  import {
    windowMinimize,
    windowToggleMaximize,
    windowClose,
    windowHideToTray,
    windowStartDrag,
    windowState,
  } from '../ipc';
  import AppDialog from './AppDialog.svelte';

  type MenuItem =
    | { kind: 'item'; label: string; action: () => void }
    | { kind: 'separator' };

  type MenuName = 'Firestarter';

  let aboutOpen = $state(false);

  const firestarterMenu: MenuItem[] = [
    { kind: 'item', label: 'Dashboard', action: () => route.goStart() },
    { kind: 'separator' },
    { kind: 'item', label: 'Sync', action: () => route.goSync() },
    { kind: 'item', label: 'Extensions', action: () => route.goExtensions() },
    { kind: 'separator' },
    { kind: 'item', label: 'Settings…', action: () => route.goSettings() },
    { kind: 'item', label: 'About', action: () => { aboutOpen = true; } },
    { kind: 'separator' },
    { kind: 'item', label: 'Exit', action: () => { windowClose().catch(() => {}); } },
  ];

  const chrome = $derived(theme.chrome);

  /** Route caption in title bar — sentence case vs uppercase from chrome. */
  const routeCaption = $derived.by(() => {
    switch (route.current.name) {
      case 'start': return 'Start';
      case 'projects': return 'Projects';
      case 'project': return 'Project';
      case 'project-pipelines': return 'Pipelines';
      case 'pipelines-cache': return 'Cached pipelines';
      case 'mr': return 'Merge request';
      case 'mr-mine': return 'Authored';
      case 'mr-reviewer': return 'Reviewing';
      case 'sync': return 'Sync';
      case 'extensions': return 'Extensions';
      case 'settings': return 'Settings';
      case 'stats': return 'Statistics';
    }
  });

  const routeCaptionDisplay = $derived(
    chrome.titleBarRouteSentenceCase ? routeCaption : routeCaption.toUpperCase(),
  );

  let maximized = $state(false);
  let openMenu = $state<MenuName | null>(null);

  async function refreshState() {
    try {
      const s = await windowState();
      maximized = s.maximized;
    } catch {
      // bridge not ready yet
    }
  }

  $effect(() => {
    refreshState();
  });

  $effect(() => {
    if (openMenu === null) return;
    function onDown(e: MouseEvent) {
      const target = e.target as HTMLElement | null;
      if (target?.closest('[data-menu-root]')) return;
      openMenu = null;
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') openMenu = null;
    }
    window.addEventListener('mousedown', onDown);
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('mousedown', onDown);
      window.removeEventListener('keydown', onKey);
    };
  });

  async function onMinimize() { await windowMinimize(); }
  async function onToggleMaximize() {
    const r = await windowToggleMaximize();
    maximized = r.maximized;
  }
  async function onClose() { await windowHideToTray(); }

  let lastPointerDown = 0;
  const DOUBLE_CLICK_MS = 400;

  function onDragPointerDown(e: PointerEvent) {
    if (e.button !== 0) return;
    const target = e.target as HTMLElement | null;
    if (target?.closest('button')) return;
    if (target?.closest('[data-menu-root]')) return;
    e.preventDefault();
    const now = performance.now();
    if (now - lastPointerDown < DOUBLE_CLICK_MS) {
      lastPointerDown = 0;
      onToggleMaximize();
      return;
    }
    lastPointerDown = now;
    windowStartDrag().catch(() => {});
  }

  function onMenuClick(name: MenuName) {
    openMenu = openMenu === name ? null : name;
  }

  function onMenuEnter(name: MenuName) {
    if (openMenu !== null && openMenu !== name) openMenu = name;
  }

  function onItemClick(item: Extract<MenuItem, { kind: 'item' }>) {
    openMenu = null;
    item.action();
  }
</script>

<div
  class="relative flex items-stretch border-b border-hair bg-paper"
  class:h-8={chrome.warmTitleBar}
  class:h-[30px]={!chrome.warmTitleBar}
  class:border-b-[0.5px]={chrome.warmTitleBar}
  class:text-[11.5px]={chrome.warmTitleBar}
  class:text-subink={chrome.warmTitleBar}
  class:text-[11px]={!chrome.warmTitleBar}
  class:font-mono={!chrome.warmTitleBar}
  style={chrome.warmTitleBar ? 'border-bottom-color:var(--hair)' : undefined}
  onpointerdown={onDragPointerDown}
>
  <div
    class="flex items-stretch"
    class:font-sans={chrome.warmTitleBar}
    class:px-2={chrome.warmTitleBar}
    data-menu-root
  >
    {@render menuTrigger('Firestarter', true)}
  </div>

  <div class="ml-auto flex flex-1 items-center justify-end">
    <span
      class="px-3 font-sans"
      class:text-subink={chrome.warmTitleBar}
      class:text-mute={!chrome.warmTitleBar}
      class:px-4={!chrome.warmTitleBar}
      class:tracking-[0.02em]={chrome.warmTitleBar}
      style={!chrome.warmTitleBar ? 'letter-spacing:.08em' : undefined}
    >{routeCaptionDisplay}</span>
    <div class="flex items-stretch">
      <button
        class="flex w-[46px] items-center justify-center hover:bg-panel-2"
        class:h-8={chrome.warmTitleBar}
        class:h-[30px]={!chrome.warmTitleBar}
        class:text-subink={chrome.warmTitleBar}
        class:text-[11px]={chrome.warmTitleBar}
        class:text-mute={!chrome.warmTitleBar}
        aria-label="minimize"
        type="button"
        tabindex="-1"
        onclick={onMinimize}
      >—</button>
      <button
        class="flex w-[46px] items-center justify-center hover:bg-panel-2"
        class:h-8={chrome.warmTitleBar}
        class:h-[30px]={!chrome.warmTitleBar}
        class:text-subink={chrome.warmTitleBar}
        class:text-[11px]={chrome.warmTitleBar}
        class:text-mute={!chrome.warmTitleBar}
        aria-label={maximized ? 'restore' : 'maximize'}
        type="button"
        tabindex="-1"
        onclick={onToggleMaximize}
      >{maximized ? '❐' : '▢'}</button>
      <button
        class="flex w-[46px] items-center justify-center hover:bg-danger-soft hover:text-danger"
        class:h-8={chrome.warmTitleBar}
        class:h-[30px]={!chrome.warmTitleBar}
        class:text-ink={chrome.warmTitleBar}
        class:text-[11px]={chrome.warmTitleBar}
        class:text-mute={!chrome.warmTitleBar}
        aria-label="close"
        type="button"
        tabindex="-1"
        onclick={onClose}
      >×</button>
    </div>
  </div>
</div>

<AppDialog bind:open={aboutOpen} title="Firestarter" variant="alert">
  <div class="space-y-4 pt-1 pb-2 leading-relaxed">
    <p>
      A lightweight desktop companion for day-to-day work against GitLab and Jenkins.
      It keeps a local cache of the projects, branches and merge requests you care about
      so navigation stays fast even on large servers.
    </p>
    <ul class="list-disc space-y-2 pl-5">
      <li>Browse projects, branches and merge requests with filters and search.</li>
      <li>Review the merge requests you authored or are assigned to in one place.</li>
      <li>Trigger and follow Jenkins pipelines without leaving the app.</li>
      <li>Open projects directly in your IDE, terminal or file explorer.</li>
      <li>Run reusable extensions across projects and inspect their logs.</li>
      <li>Track outbound API traffic — see <em>Statistics</em>.</li>
    </ul>
  </div>
  {#snippet footerLeft()}
    <button
      type="button"
      class="focus-ring border border-hair bg-paper px-3 py-1.5 text-[12px] text-ink transition-colors hover:bg-panel-2"
      style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono)"
      onclick={() => { aboutOpen = false; route.goStats(); }}
    >Statistics</button>
  {/snippet}
</AppDialog>

{#snippet menuTrigger(name: MenuName, isLogo: boolean)}
  {@const isOpen = openMenu === name}
  <div class="relative flex items-center">
    <button
      type="button"
      tabindex="-1"
      class="flex items-center gap-2 transition-colors"
      class:h-8={chrome.warmTitleBar}
      class:h-[30px]={!chrome.warmTitleBar}
      class:rounded={chrome.warmTitleBar && !isLogo}
      class:px-[9px]={chrome.warmTitleBar && !isLogo}
      class:py-[5px]={chrome.warmTitleBar && !isLogo}
      class:px-3={!chrome.warmTitleBar}
      class:hover:bg-panel-2={!chrome.warmTitleBar || !isLogo}
      class:text-subink={!isLogo && !isOpen}
      class:hover:text-ink={!isLogo}
      class:bg-panel-2={isOpen && (!chrome.warmTitleBar || !isLogo)}
      class:text-ink={(isOpen && !chrome.warmTitleBar && !isLogo) || (chrome.warmTitleBar && isLogo)}
      onclick={() => onMenuClick(name)}
      onpointerenter={() => onMenuEnter(name)}
    >
      {#if isLogo}
        {#if chrome.warmTitleBar}
          <svg class="shrink-0" width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
            <path
              fill="var(--accent)"
              d="M8 1.5 C 10.5 4, 12.5 5.5, 12.5 9 A 4.5 4.5 0 1 1 3.5 9 C 3.5 6.5, 5 5, 6 3.5 C 6.5 5, 7.5 5.5, 8 6.5 C 8.5 5, 8 3, 8 1.5 Z"
            />
          </svg>
          <span class="text-ink" style="font-family:var(--font-serif); font-style:italic; font-size:13px">
            Firestarter
          </span>
        {:else}
          <span class="inline-block size-[10px] rounded-[2px] bg-accent"></span>
          <span class="text-ink" style="letter-spacing:.14em; font-weight:500">FIRESTARTER</span>
        {/if}
      {:else}
        {name}
      {/if}
    </button>
    {#if isOpen}
      <div
        class="absolute left-0 top-full z-50 min-w-[200px] border border-hair bg-paper py-1 shadow-[var(--shadow)]"
        style={chrome.warmTitleBar ? 'border-radius:var(--radius-sm)' : undefined}
      >
        {#each firestarterMenu as mi, i (i)}
          {#if mi.kind === 'separator'}
            <div class="my-1 border-t border-hair"></div>
          {:else}
            <button
              type="button"
              tabindex="-1"
              class="flex w-full items-center px-3 py-1.5 text-left text-[12px] text-ink hover:bg-accent-wash hover:text-accent"
              onclick={() => onItemClick(mi)}
            >{mi.label}</button>
          {/if}
        {/each}
      </div>
    {/if}
  </div>
{/snippet}
