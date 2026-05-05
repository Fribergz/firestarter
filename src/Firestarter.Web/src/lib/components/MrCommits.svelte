<script lang="ts">
  import type { MergeRequestCommit } from '../api';
  import { timeAgo } from '../format';

  interface Props { commits: MergeRequestCommit[] }
  let { commits }: Props = $props();

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const monoSmall = 'font-family:var(--font-mono); font-size:11.5px';
</script>

{#if commits.length === 0}
  <div class="border border-hair bg-paper p-8 text-center" style="border-radius:var(--radius-lg)">
    <p class="text-mute" style="font-family:var(--font-mono); font-size:11.5px; letter-spacing:.08em">NO COMMITS</p>
  </div>
{:else}
  <div class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
    <div class="border-b border-hair px-5 py-3" style={labelFont}>COMMITS · {commits.length}</div>
    <ul class="divide-y divide-hair">
      {#each commits as c (c.id)}
        <li class="flex items-start gap-3 px-5 py-3">
          <a href={c.webUrl ?? ''} target="_blank" rel="noreferrer" class="shrink-0 text-accent" style={monoSmall} title={c.id}>
            {c.shortId}
          </a>
          <div class="min-w-0 flex-1">
            <div class="text-ink" style="font-size:12.5px">{c.title}</div>
            <div class="text-mute" style={monoSmall}>{c.authorName ?? '—'} · {timeAgo(c.authoredDate)}</div>
          </div>
        </li>
      {/each}
    </ul>
  </div>
{/if}
