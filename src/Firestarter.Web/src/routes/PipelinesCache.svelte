<script lang="ts">
  import { onMount } from 'svelte';
  import { api, type CachedProjectPipelines } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import NavRail from '../lib/components/NavRail.svelte';
  import JenkinsPipelineList from '../lib/components/JenkinsPipelineList.svelte';
  import { timeAgo } from '../lib/format';

  let items = $state<CachedProjectPipelines[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  const chrome = $derived(theme.chrome);
  const pageTitle = $derived(
    chrome.sectionLabelCasing === 'sentence' ? 'Cached pipelines' : 'CACHED PIPELINES',
  );

  onMount(() => {
    void load();
  });

  async function load() {
    loading = true;
    error = null;
    try {
      const res = await api.listCachedProjectPipelines();
      items = res.items ?? [];
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    } finally {
      loading = false;
    }
  }
</script>

<div class="flex min-h-0 flex-1">
  <NavRail />
  <div class="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
    <PageHeader title={pageTitle} breadcrumbSyncing={false} />
    <div class="min-h-0 flex-1 overflow-auto p-6 bg-bg">
      {#if loading}
        <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
      {:else if error}
        <p class="text-danger">{error}</p>
      {:else if items.length === 0}
        <p class="text-mute" style="font-size:12px">
          No pipeline cache in the database yet. Open a project’s Pipelines tab to load Jenkins and store a snapshot.
          GitLab sync stays under Tools → Sync.
        </p>
      {:else}
        <p class="mb-6 text-mute" style="font-size:12px">
          Local snapshots only (no live Jenkins). Open a project for a fresh list.
        </p>
        <div class="space-y-6">
          {#each items as row (row.projectId)}
            <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
              <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
                <div class="min-w-0">
                  <button
                    type="button"
                    class="truncate text-left text-accent hover:underline"
                    style="font-family:var(--font-mono); font-size:12px"
                    onclick={() => route.goProjectPipelines(row.projectId)}
                  >
                    {row.pathWithNamespace}
                  </button>
                  <div class="mt-0.5 text-mute" style="font-family:var(--font-mono); font-size:10.5px">
                    Cached {row.cachedAt ? timeAgo(row.cachedAt) : '—'} · {row.pipelines.length} build{row.pipelines.length === 1 ? '' : 's'}
                  </div>
                </div>
                <button
                  type="button"
                  class="shrink-0 text-accent hover:underline"
                  style="font-family:var(--font-mono); font-size:10.5px"
                  onclick={() => route.goProjectPipelines(row.projectId)}
                >
                  Live pipelines →
                </button>
              </div>
              <JenkinsPipelineList
                condensed
                includeHeading={false}
                pipelines={row.pipelines}
                jenkinsConfigured={true}
                error={null}
              />
            </section>
          {/each}
        </div>
      {/if}
    </div>
  </div>
</div>
