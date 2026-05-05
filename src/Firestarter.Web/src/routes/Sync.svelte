<script lang="ts">
  import { onMount } from 'svelte';
  import { api, type SyncScope, type SyncStatus } from '../lib/api';
  import { theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import Pill from '../lib/components/Pill.svelte';
  import AppDialog from '../lib/components/AppDialog.svelte';

  let status = $state<SyncStatus | null>(null);
  let loadError = $state<string | null>(null);
  let starting = $state(false);
  let startError = $state<string | null>(null);
  let resetting = $state(false);
  let resetError = $state<string | null>(null);
  let resetMessage = $state<string | null>(null);
  let resetConfirmOpen = $state(false);

  async function refresh() {
    try {
      status = await api.getSyncStatus();
      loadError = null;
    } catch (err) {
      loadError = err instanceof Error ? err.message : String(err);
    }
  }

  async function start(scope: SyncScope) {
    starting = true;
    startError = null;
    try {
      await api.startSync({ scope, reason: 'ui' });
      await refresh();
    } catch (err) {
      startError = err instanceof Error ? err.message : String(err);
    } finally {
      starting = false;
    }
  }

  async function resetJenkinsHooks() {
    resetting = true;
    resetError = null;
    resetMessage = null;
    try {
      const { reset } = await api.resetJenkinsJobPaths();
      resetMessage =
        reset === 0
          ? 'No projects had a cached Jenkins job path.'
          : `Cleared Jenkins job path on ${reset} project${reset === 1 ? '' : 's'}.`;
    } catch (err) {
      resetError = err instanceof Error ? err.message : String(err);
    } finally {
      resetting = false;
    }
  }

  onMount(() => {
    refresh();
  });

  // Adaptive polling: 1s while a sync is actively running so the UI stays live, else 5s to keep
  // IPC chatter (and Photino's WebMessage console output) from spamming when nothing is happening.
  $effect(() => {
    const ms = status?.state === 'Running' ? 1000 : 5000;
    const timer = setInterval(refresh, ms);
    return () => clearInterval(timer);
  });

  const stateTone = $derived.by<'passed' | 'running' | 'failed' | 'neutral'>(() => {
    switch (status?.state) {
      case 'Running': return 'running';
      case 'Error': return 'failed';
      case 'Idle': return 'passed';
      default: return 'neutral';
    }
  });

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const chrome = $derived(theme.chrome);
</script>

<div class="flex min-h-0 flex-1 flex-col">
  <PageHeader title={chrome.sectionLabelCasing === 'sentence' ? 'Sync' : 'SYNC'} />

  <div class="min-h-0 flex-1 overflow-auto bg-bg p-6">
    <div class="mx-auto max-w-3xl space-y-4">
      <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
        <div class="flex items-start justify-between">
          <div>
            <div style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Status' : 'STATUS'}</div>
            <div class="mt-2 flex items-center gap-3">
              <Pill tone={stateTone}>{status?.state ?? '…'}</Pill>
              {#if status?.currentScope}
                <span class="text-subink" style="font-family:var(--font-mono); font-size:11.5px">
                  {status.currentScope}
                </span>
              {/if}
            </div>
          </div>
          <div class="text-right text-mute" style="font-family:var(--font-mono); font-size:11px">
            <div>queue: {status?.queueDepth ?? 0}</div>
            {#if status?.lastFinishedAt}
              <div class="mt-0.5">last: {new Date(status.lastFinishedAt).toLocaleTimeString()}</div>
            {/if}
          </div>
        </div>

        {#if status?.currentItem}
          <div class="mt-3 truncate text-subink" style="font-family:var(--font-mono); font-size:12px">
            <span class="text-mute">current</span> · {status.currentItem}
          </div>
        {/if}
        {#if status && status.processed > 0}
          <div class="mt-1 text-subink" style="font-family:var(--font-mono); font-size:12px">
            <span class="text-mute">processed</span> · {status.processed}{status.total ? ` / ${status.total}` : ''}
          </div>
        {/if}

        {#if status?.lastError}
          <div class="mt-3 border border-danger p-2 text-danger"
               style="border-radius:var(--radius); background:var(--danger-soft); font-size:12.5px">
            {status.lastError}
          </div>
        {/if}
        {#if loadError}
          <div class="mt-3 text-danger" style="font-size:12.5px">
            Could not load status: {loadError}
          </div>
        {/if}
      </section>

      <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
        <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Actions' : 'ACTIONS'}</h2>
        <div class="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            class="border border-accent px-3 py-1.5 text-[12px] disabled:opacity-50"
            style="border-radius:var(--radius); color:var(--paper); background:var(--accent)"
            disabled={starting || status?.state === 'Running'}
            onclick={() => start('FullProjects')}
          >
            Full project sync
          </button>
          <button
            type="button"
            class="border border-hair bg-paper px-3 py-1.5 text-[12px] text-ink transition-colors hover:bg-panel-2 disabled:opacity-50"
            style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono)"
            disabled={resetting}
            onclick={() => { resetMessage = null; resetError = null; resetConfirmOpen = true; }}
          >
            Reset Jenkins hook cache
          </button>
        </div>
        {#if startError}
          <p class="mt-3 text-danger" style="font-size:12.5px">Could not start sync: {startError}</p>
        {/if}
        {#if resetError}
          <p class="mt-3 text-danger" style="font-size:12.5px">Could not reset: {resetError}</p>
        {/if}
        {#if resetMessage}
          <p class="mt-3 text-subink" style="font-family:var(--font-mono); font-size:11.5px">{resetMessage}</p>
        {/if}
      </section>
    </div>
  </div>
</div>

<AppDialog
  bind:open={resetConfirmOpen}
  title="Reset Jenkins hook cache"
  variant="confirm"
  primaryLabel="Reset"
  cancelLabel="Cancel"
  onPrimary={resetJenkinsHooks}
>
  <p class="leading-relaxed">
    Clear the cached Jenkins job path on every project? They will be re-derived from project hooks
    on the next full sync.
  </p>
</AppDialog>
