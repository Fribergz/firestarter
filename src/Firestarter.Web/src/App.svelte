<script lang="ts">
  import { onMount } from 'svelte';
  import { route } from './lib/stores/route.svelte';
  import { theme } from './lib/stores/theme.svelte';
  import TitleBar from './lib/components/TitleBar.svelte';
  import StatusBar from './lib/components/StatusBar.svelte';
  import ResizeHandles from './lib/components/ResizeHandles.svelte';
  import CommandPaletteOverlay from './lib/components/CommandPaletteOverlay.svelte';

  import Start from './routes/Start.svelte';
  import Projects from './routes/Projects.svelte';
  import ProjectDetail from './routes/ProjectDetail.svelte';
  import ProjectPipelines from './routes/ProjectPipelines.svelte';
  import MergeRequestDetail from './routes/MergeRequestDetail.svelte';
  import MergeRequestsMine from './routes/MergeRequestsMine.svelte';
  import MergeRequestsReviewer from './routes/MergeRequestsReviewer.svelte';
  import Sync from './routes/Sync.svelte';
  import PipelinesCache from './routes/PipelinesCache.svelte';
  import Extensions from './routes/Extensions.svelte';
  import Settings from './routes/Settings.svelte';
  import Stats from './routes/Stats.svelte';

  let cmdOpen = $state(false);

  onMount(() => {
    theme.hydrate();
  });

  // Browser-level shortcuts to swallow when Ctrl (or Cmd on macOS — harmless on Windows) is held.
  // We deliberately do NOT block edit-clipboard combos: Ctrl+C / Ctrl+V always pass through, and
  // Ctrl+A / Ctrl+X / Ctrl+Z / Ctrl+Y are left alone so text inputs stay usable.
  const blockedCtrlKeys = new Set([
    'p', 'r', 's', 'g', 'w', 't', 'n', 'l', 'h', 'j', 'd', 'u', 'o', 'e', 'b', 'i', 'm', 'q',
    '0', '=', '+', '-',
  ]);
  const blockedFunctionKeys = new Set(['F1', 'F3', 'F5', 'F6', 'F7', 'F11', 'F12']);

  $effect(() => {
    function onKeyDown(e: KeyboardEvent) {
      // Function-key browser shortcuts (find next, reload, fullscreen, devtools, etc.).
      if (blockedFunctionKeys.has(e.key)) {
        e.preventDefault();
        return;
      }

      const ctrl = e.ctrlKey || e.metaKey;
      if (!ctrl) return;

      const k = e.key.toLowerCase();

      // Ctrl+F → command palette overlay (or focus the inline finder on the start page).
      if (k === 'f' && !e.shiftKey && !e.altKey) {
        e.preventDefault();
        if (route.current.name === 'start') {
          const input = document.getElementById('start-find-input') as HTMLInputElement | null;
          input?.focus();
          input?.select();
        } else {
          cmdOpen = true;
        }
        return;
      }

      // Ctrl+Shift+I / Ctrl+Shift+J / Ctrl+Shift+P — devtools, console, command palette in browsers.
      if (e.shiftKey && (k === 'i' || k === 'j' || k === 'p')) {
        e.preventDefault();
        return;
      }

      // Ctrl+A / X / Z / Y — only valid when focus is in an editable element. Outside an input
      // they would select all page text / cut nothing / undo browser nav, which we don't want.
      const editKeys = new Set(['a', 'x', 'z', 'y']);
      if (editKeys.has(k) && !isEditableTarget(e.target)) {
        e.preventDefault();
        return;
      }

      if (blockedCtrlKeys.has(k)) {
        e.preventDefault();
      }
    }

    function isEditableTarget(target: EventTarget | null): boolean {
      if (!(target instanceof HTMLElement)) return false;
      const tag = target.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
      return target.isContentEditable;
    }
    window.addEventListener('keydown', onKeyDown, true);
    return () => window.removeEventListener('keydown', onKeyDown, true);
  });
</script>

<div class="flex h-full flex-col overflow-hidden bg-bg text-ink">
  <TitleBar />
  <div class="flex min-h-0 flex-1">
    <main class="relative flex min-w-0 flex-1 flex-col overflow-hidden">
      {#if route.current.name === 'start'}
        <Start />
      {:else if route.current.name === 'projects'}
        <Projects />
      {:else if route.current.name === 'project'}
        <ProjectDetail projectId={route.current.projectId} />
      {:else if route.current.name === 'project-pipelines'}
        <ProjectPipelines projectId={route.current.projectId} />
      {:else if route.current.name === 'pipelines-cache'}
        <PipelinesCache />
      {:else if route.current.name === 'mr'}
        <MergeRequestDetail
          projectId={route.current.projectId}
          mrId={route.current.mrId}
          listParent={route.current.listParent}
        />
      {:else if route.current.name === 'mr-mine'}
        <MergeRequestsMine />
      {:else if route.current.name === 'mr-reviewer'}
        <MergeRequestsReviewer />
      {:else if route.current.name === 'sync'}
        <Sync />
      {:else if route.current.name === 'extensions'}
        <Extensions />
      {:else if route.current.name === 'settings'}
        <Settings />
      {:else if route.current.name === 'stats'}
        <Stats />
      {/if}
    </main>
  </div>
  <StatusBar />
  <ResizeHandles />
</div>

<CommandPaletteOverlay bind:open={cmdOpen} />
