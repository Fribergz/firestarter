<script lang="ts">
  import { onMount } from 'svelte';
  import { theme } from '../stores/theme.svelte';
  import { route } from '../stores/route.svelte';
  import { updateStatus } from '../stores/updateStatus.svelte';
  import { api } from '../api';
  import WorkspaceOpenFab from './WorkspaceOpenFab.svelte';
  import AppDialog from './AppDialog.svelte';

  const routeLabel = $derived.by(() => {
    switch (route.current.name) {
      case 'start': return 'START';
      case 'projects': return 'PROJECTS';
      case 'project': return 'PROJECT';
      case 'project-pipelines': return 'PIPELINES';
      case 'pipelines-cache': return 'PIPELINES · CACHE';
      case 'mr': return 'MR';
      case 'mr-mine': return 'AUTHORED';
      case 'mr-reviewer': return 'REVIEWING';
      case 'sync': return 'SYNC';
      case 'extensions': return 'EXTENSIONS';
      case 'settings': return 'SETTINGS';
      case 'stats': return 'STATISTICS';
    }
  });

  function fmtClock(d: Date): string {
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  let nowLabel = $state(fmtClock(new Date()));

  onMount(() => {
    // Align the first tick to the start of the next minute so the displayed time flips exactly
    // when the wall clock minute rolls over, then keep ticking every 60s.
    const now = new Date();
    const msToNextMinute = 60_000 - (now.getSeconds() * 1000 + now.getMilliseconds());
    let interval: ReturnType<typeof setInterval> | null = null;
    const timeout = setTimeout(() => {
      nowLabel = fmtClock(new Date());
      interval = setInterval(() => {
        nowLabel = fmtClock(new Date());
      }, 60_000);
    }, msToNextMinute);

    return () => {
      clearTimeout(timeout);
      if (interval) clearInterval(interval);
    };
  });

  const chrome = $derived(theme.chrome);
  const statusBg = $derived(chrome.statusBarBackground === 'panel-2' ? 'var(--panel-2)' : 'var(--paper)');

  const update = $derived(updateStatus.value);
  let updateConfirmOpen = $state(false);
  let updateError = $state<string | null>(null);
  let applying = $state(false);

  async function applyUpdate() {
    applying = true;
    updateError = null;
    try {
      const r = await api.applyUpdate();
      if (!r.ok) updateError = r.error ?? 'Update failed.';
      // On success the host process is closing — the page will go away with it.
    } catch (err) {
      updateError = err instanceof Error ? err.message : String(err);
    } finally {
      applying = false;
    }
  }
</script>

<footer
  class="relative z-20 flex min-h-[28px] items-center gap-4 border-t border-hair px-4 py-1.5"
  style="font-family:var(--font-mono); font-size:10.5px; letter-spacing:.06em; background:{statusBg}; color:var(--subink)"
>
  <div class="flex min-w-0 flex-1 items-center gap-4">
    {#if update?.updateAvailable}
      <button
        type="button"
        class="update-pill inline-flex items-center gap-1.5 px-2 py-0.5"
        title="Update available: {update.latestVersion} (current {update.currentVersion}). Click to install."
        onclick={() => { updateError = null; updateConfirmOpen = true; }}
      >
        <span class="update-dot" aria-hidden="true"></span>
        UPDATE {update.latestVersion}
      </button>
    {:else if chrome.statusReadyPresentation === 'pill-on-accent'}
      <span class="inline-flex items-center gap-2 px-2 py-0.5" style="background:var(--accent); color:var(--bg)">
        READY
      </span>
    {:else if chrome.statusReadyPresentation === 'upper-accent'}
      <span class="uppercase text-accent" style="font-weight:500">READY</span>
    {:else}
      <span class="text-accent" style="font-family:var(--font-serif); font-style:italic; font-size:11px">ready</span>
    {/if}

    <span>{routeLabel}</span>
    <span class="text-mute">·</span>
    <span class="text-mute">[↑][↓] NAVIGATE</span>
    <span class="text-mute">·</span>
    <span class="text-mute">[↵] OPEN</span>
  </div>

  <div class="flex shrink-0 items-center">
    <WorkspaceOpenFab />
    <span
      class="mx-1.5 h-2.5 w-px shrink-0 self-center"
      style="background:var(--hair)"
      aria-hidden="true"
    ></span>
    <span class="shrink-0 text-mute tabular-nums">{nowLabel}</span>
  </div>
</footer>

<AppDialog
  bind:open={updateConfirmOpen}
  title="Install update"
  variant="confirm"
  primaryLabel={applying ? 'Updating…' : 'Update'}
  cancelLabel="Cancel"
  onPrimary={applyUpdate}
>
  <p class="leading-relaxed">
    {#if update}
      A new version <strong>{update.latestVersion}</strong> is available. The application will close,
      download and install the update, then relaunch automatically.
    {:else}
      A new version is available. The application will close, install the update, and relaunch.
    {/if}
  </p>
  {#if updateError}
    <p class="mt-3 text-danger" style="font-size:12.5px">{updateError}</p>
  {/if}
</AppDialog>

<style>
  .update-pill {
    background: var(--accent);
    color: var(--bg);
    border: 0;
    cursor: pointer;
    font-family: inherit;
    font-size: inherit;
    letter-spacing: inherit;
    transition: opacity 120ms;
  }
  .update-pill:hover { opacity: 0.85; }
  .update-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--bg);
    box-shadow: 0 0 0 2px rgba(255, 255, 255, 0.35);
    animation: update-pulse 1.6s ease-in-out infinite;
  }
  @keyframes update-pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.55; }
  }
</style>
