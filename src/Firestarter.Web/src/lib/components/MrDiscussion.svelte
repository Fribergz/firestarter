<script lang="ts">
  import { marked } from 'marked';
  import type { MergeRequestDiscussion } from '../api';
  import { timeAgo } from '../format';

  interface Props {
    discussions: MergeRequestDiscussion[];
    /** When true, system notes (label changes etc.) are included; otherwise only user-authored notes. */
    showSystem?: boolean;
  }
  let { discussions, showSystem = $bindable(false) }: Props = $props();

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const monoSmall = 'font-family:var(--font-mono); font-size:11.5px';

  function renderMd(body: string | null): string {
    if (!body || body.trim().length === 0) return '';
    return marked.parse(body, { gfm: true, breaks: true, async: false }) as string;
  }

  const visible = $derived(
    discussions
      .map(d => ({
        ...d,
        notes: showSystem ? d.notes : d.notes.filter(n => !n.system),
      }))
      .filter(d => d.notes.length > 0),
  );
</script>

<div class="space-y-3">
  <div class="flex items-center justify-between">
    <span style={labelFont}>DISCUSSIONS · {visible.length}</span>
    <label class="flex items-center gap-2 text-mute" style={monoSmall}>
      <input type="checkbox" bind:checked={showSystem} />
      show system notes
    </label>
  </div>

  {#if visible.length === 0}
    <div class="border border-hair bg-paper p-8 text-center" style="border-radius:var(--radius-lg)">
      <p class="text-mute" style="font-family:var(--font-mono); font-size:11.5px; letter-spacing:.08em">NO DISCUSSION</p>
    </div>
  {:else}
    {#each visible as d (d.id)}
      <section class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
        {#each d.notes as n, i (n.id)}
          <article class="px-4 py-3" class:border-t={i > 0} class:border-hair={i > 0}>
            <header class="flex items-center justify-between">
              <div class="flex items-center gap-2 text-subink" style="font-size:12.5px">
                <span class="text-ink" style="font-weight:500">{n.author ?? '—'}</span>
                {#if n.system}
                  <span class="text-mute" style={monoSmall}>(system)</span>
                {/if}
                <span class="text-mute" style={monoSmall}>{timeAgo(n.createdAt)}</span>
              </div>
              {#if n.resolvable}
                <span class="text-mute" style={monoSmall}>{n.resolved ? 'resolved' : 'unresolved'}</span>
              {/if}
            </header>
            <div class="gitlab-md mt-2 text-ink" style="font-size:13px; line-height:1.55">
              {@html renderMd(n.body)}
            </div>
          </article>
        {/each}
      </section>
    {/each}
  {/if}
</div>

<style>
  .gitlab-md :global(p) { margin: 0.4em 0; }
  .gitlab-md :global(ul),
  .gitlab-md :global(ol) { margin: 0.3em 0 0.3em 1.4em; }
  .gitlab-md :global(code) {
    font-family: var(--font-mono); background: var(--panel-2);
    padding: 0.05em 0.35em; border-radius: 3px; font-size: 0.9em;
  }
  .gitlab-md :global(pre) {
    background: var(--panel-2); padding: 0.6em 0.8em;
    border-radius: var(--radius); overflow-x: auto; font-size: 12px;
  }
  .gitlab-md :global(pre code) { background: transparent; padding: 0; }
  .gitlab-md :global(blockquote) {
    border-left: 3px solid var(--hair-strong); color: var(--subink);
    padding: 0.2em 0.8em; margin: 0.4em 0;
  }
  .gitlab-md :global(table) { border-collapse: collapse; margin: 0.4em 0; font-size: 12.5px; }
  .gitlab-md :global(th),
  .gitlab-md :global(td) { border: 1px solid var(--hair); padding: 0.3em 0.55em; }
  .gitlab-md :global(a) { color: var(--accent); }
</style>
