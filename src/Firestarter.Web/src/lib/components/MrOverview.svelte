<script lang="ts">
  import { marked } from 'marked';
  import type { MergeRequestOverview } from '../api';
  import { theme } from '../stores/theme.svelte';

  interface Props { overview: MergeRequestOverview }
  let { overview }: Props = $props();

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const monoSmall = 'font-family:var(--font-mono); font-size:11.5px';
  const chrome = $derived(theme.chrome);

  const descriptionHtml = $derived.by(() => {
    const md = (overview.description ?? '').trim();
    if (!md) return '';
    return marked.parse(md, { gfm: true, breaks: true, async: false }) as string;
  });
</script>

<div class="grid grid-cols-[1fr_320px] gap-6">
  <div class="space-y-4">
    <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
      <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Description' : 'DESCRIPTION'}</h2></div>
      {#if descriptionHtml}
        <div class="gitlab-md text-ink" style="font-size:13px; line-height:1.55">
          {@html descriptionHtml}
        </div>
      {:else}
        <p class="text-mute" style="font-size:12px">No description.</p>
      {/if}
    </section>

    <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
      <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Stats' : 'STATS'}</h2></div>
      <div class="grid grid-cols-3 gap-4" style="font-family:var(--font-mono)">
        <div>
          <div class="text-mute" style="font-size:10.5px">CHANGES</div>
          <div class="text-ink" style="font-size:18px">{overview.changesCount || '—'}</div>
        </div>
        <div>
          <div class="text-mute" style="font-size:10.5px">NOTES</div>
          <div class="text-ink" style="font-size:18px">{overview.userNotesCount}</div>
        </div>
        <div>
          <div class="text-mute" style="font-size:10.5px">DIVERGED</div>
          <div class="text-ink" style="font-size:18px">{overview.divergedCommitsCount ?? '—'}</div>
        </div>
      </div>
    </section>
  </div>

  <div class="space-y-4">
    <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
      <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Approvals' : 'APPROVALS'}</h2></div>
      {#if overview.approvals}
        {@const a = overview.approvals}
        <p class="text-subink" style="font-size:12.5px">
          {a.approved ? 'Approved' : `${a.approvalsLeft} of ${a.approvalsRequired} required`}
        </p>
        {#if a.approvedBy.length > 0}
          <p class="mt-2 text-mute" style={monoSmall}>by {a.approvedBy.join(', ')}</p>
        {/if}
        {#if !a.approved && a.suggestedApprovers.length > 0}
          <p class="mt-2 text-mute" style={monoSmall}>suggested: {a.suggestedApprovers.slice(0, 5).join(', ')}</p>
        {/if}
      {:else}
        <p class="text-mute" style="font-size:12px">Not available.</p>
      {/if}
    </section>

    <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
      <div class="mb-3"><h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Labels' : 'LABELS'}</h2></div>
      {#if overview.labels.length === 0}
        <p class="text-mute" style="font-size:12px">—</p>
      {:else}
        <div class="flex flex-wrap gap-1.5">
          {#each overview.labels as label (label)}
            <span class="border border-hair-strong px-2 py-0.5 text-subink"
                  style="border-radius:var(--radius); {monoSmall}">
              {label}
            </span>
          {/each}
        </div>
      {/if}
    </section>
  </div>
</div>

<style>
  .gitlab-md :global(h1),
  .gitlab-md :global(h2),
  .gitlab-md :global(h3),
  .gitlab-md :global(h4) {
    margin: 0.8em 0 0.4em;
    font-weight: 600;
    line-height: 1.3;
  }
  .gitlab-md :global(h1) { font-size: 1.4em; border-bottom: 1px solid var(--hair); padding-bottom: 0.2em; }
  .gitlab-md :global(h2) { font-size: 1.2em; border-bottom: 1px solid var(--hair); padding-bottom: 0.15em; }
  .gitlab-md :global(p) { margin: 0.5em 0; }
  .gitlab-md :global(ul),
  .gitlab-md :global(ol) { margin: 0.4em 0 0.4em 1.4em; }
  .gitlab-md :global(li) { margin: 0.15em 0; }
  .gitlab-md :global(code) {
    font-family: var(--font-mono); background: var(--panel-2);
    padding: 0.05em 0.35em; border-radius: 3px; font-size: 0.9em;
  }
  .gitlab-md :global(pre) {
    background: var(--panel-2); padding: 0.6em 0.8em;
    border-radius: var(--radius); overflow-x: auto; font-size: 12px;
  }
  .gitlab-md :global(pre code) { background: transparent; padding: 0; }
  .gitlab-md :global(table) { border-collapse: collapse; margin: 0.6em 0; width: 100%; font-size: 12.5px; }
  .gitlab-md :global(th),
  .gitlab-md :global(td) { border: 1px solid var(--hair); padding: 0.35em 0.6em; text-align: left; vertical-align: top; }
  .gitlab-md :global(th) { background: var(--panel-2); font-weight: 600; }
  .gitlab-md :global(blockquote) {
    border-left: 3px solid var(--hair-strong); color: var(--subink);
    padding: 0.2em 0.8em; margin: 0.5em 0;
  }
  .gitlab-md :global(a) { color: var(--accent); }
</style>
