<script lang="ts">
  import { api, type ExtensionRunLog } from '../api';
  import { theme } from '../stores/theme.svelte';

  interface Props { runId: number; autoLoad?: boolean }
  let { runId, autoLoad = false }: Props = $props();

  let log = $state<ExtensionRunLog | null>(null);
  let loading = $state(false);
  let error = $state<string | null>(null);
  let tab = $state<'stdout' | 'stderr'>('stdout');
  let loadedForId = $state<number | null>(null);

  async function load() {
    loading = true;
    error = null;
    try {
      log = await api.getRunLog(runId);
      loadedForId = runId;
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      loading = false;
    }
  }

  $effect(() => {
    if (autoLoad && loadedForId !== runId) {
      void load();
    }
  });

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const hasContent = $derived(log && ((log.stdout ?? '').length > 0 || (log.stderr ?? '').length > 0));
  const chrome = $derived(theme.chrome);
</script>

<div class="border border-hair bg-paper" style="border-radius:var(--radius)">
  <div class="flex items-center gap-3 border-b border-hair px-3 py-2">
    <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Output' : 'OUTPUT'}</span>
    {#if log}
      <div class="flex items-center gap-1">
        {#each ['stdout', 'stderr'] as t (t)}
          {@const active = tab === t}
          <button
            type="button"
            onclick={() => (tab = t as 'stdout' | 'stderr')}
            class="px-2 py-0.5 text-[11px]"
            class:bg-panel-2={active}
            class:text-ink={active}
            class:text-subink={!active}
            style="border-radius:var(--radius); font-family:var(--font-mono); letter-spacing:.06em"
          >
            {t}
          </button>
        {/each}
      </div>
    {/if}
    <button
      type="button"
      onclick={load}
      class="ml-auto text-mute hover:text-ink"
      style="font-family:var(--font-mono); font-size:11px"
    >
      {loading ? 'loading…' : log ? 'refresh' : 'load'}
    </button>
  </div>

  {#if error}
    <p class="p-3 text-danger" style="font-size:12px">{error}</p>
  {:else if !log}
    <p class="p-3 text-mute" style="font-size:12px">
      {loading ? 'Loading…' : 'Output not loaded.'}
    </p>
  {:else if !hasContent}
    <p class="p-3 text-mute" style="font-size:12px">Log file is empty.</p>
  {:else}
    {@const content = tab === 'stdout' ? log.stdout : log.stderr}
    {@const truncated = tab === 'stdout' ? log.stdoutTruncated : log.stderrTruncated}
    {#if truncated}
      <p class="border-b border-hair px-3 py-1 text-mute"
         style="font-family:var(--font-mono); font-size:10.5px">
        (truncated — showing tail)
      </p>
    {/if}
    {#if content && content.length > 0}
      <pre class="overflow-x-auto bg-panel-2 p-3 text-subink"
           style="border-radius:0 0 var(--radius) var(--radius); font-family:var(--font-mono); font-size:11px; line-height:1.5; white-space:pre; word-break:normal">{content}</pre>
    {:else}
      <p class="p-3 text-mute" style="font-size:12px">({tab} is empty)</p>
    {/if}
  {/if}
</div>
