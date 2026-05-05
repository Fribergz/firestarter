<script lang="ts">
  import type { MergeRequestFileChange } from '../api';

  interface Props { changes: MergeRequestFileChange[] }
  let { changes }: Props = $props();

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const monoSmall = 'font-family:var(--font-mono); font-size:11.5px';

  /** Per-file collapse state — defaults to open for the first 5, collapsed beyond. */
  let openFiles = $state<Set<string>>(new Set(changes.slice(0, 5).map(c => c.newPath || c.oldPath)));

  function toggle(key: string) {
    const next = new Set(openFiles);
    if (next.has(key)) next.delete(key); else next.add(key);
    openFiles = next;
  }

  function statusLabel(c: MergeRequestFileChange): string {
    if (c.newFile) return 'added';
    if (c.deletedFile) return 'deleted';
    if (c.renamedFile) return 'renamed';
    return 'modified';
  }

  function statusColor(c: MergeRequestFileChange): string {
    if (c.newFile) return 'var(--ok)';
    if (c.deletedFile) return 'var(--danger)';
    return 'var(--mute)';
  }

  /** Color-classify a single diff line based on its first character. */
  function lineKind(line: string): 'add' | 'del' | 'meta' | 'context' {
    if (!line) return 'context';
    const c = line[0];
    if (c === '+' && !line.startsWith('+++')) return 'add';
    if (c === '-' && !line.startsWith('---')) return 'del';
    if (c === '@' || c === '+' || c === '-' || c === 'd' || c === 'i' || c === 'n') return 'meta';
    return 'context';
  }

  function diffLines(diff: string): string[] {
    return (diff ?? '').split(/\r?\n/);
  }
</script>

{#if changes.length === 0}
  <div class="border border-hair bg-paper p-8 text-center" style="border-radius:var(--radius-lg)">
    <p class="text-mute" style="font-family:var(--font-mono); font-size:11.5px; letter-spacing:.08em">NO CHANGES</p>
  </div>
{:else}
  <div class="space-y-3">
    {#each changes as c (c.newPath || c.oldPath)}
      {@const key = c.newPath || c.oldPath}
      {@const open = openFiles.has(key)}
      <div class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
        <button
          type="button"
          onclick={() => toggle(key)}
          class="flex w-full items-center justify-between gap-3 px-4 py-2 text-left hover:bg-panel-2"
          style="border-radius:var(--radius-lg) var(--radius-lg) 0 0"
        >
          <span class="truncate text-ink" style={monoSmall} title={key}>
            {c.renamedFile ? `${c.oldPath} → ${c.newPath}` : key}
          </span>
          <span class="shrink-0" style="font-family:var(--font-mono); font-size:10.5px; color:{statusColor(c)}; text-transform:uppercase; letter-spacing:.08em">
            {statusLabel(c)}
          </span>
        </button>
        {#if open}
          <div class="overflow-x-auto border-t border-hair">
            <pre class="m-0 p-3 text-ink" style="font-family:var(--font-mono); font-size:11.5px; line-height:1.4; white-space:pre">{#each diffLines(c.diff) as ln, i (i)}<span class="diff-line diff-{lineKind(ln)}">{ln}
</span>{/each}</pre>
          </div>
        {/if}
      </div>
    {/each}
  </div>
{/if}

<style>
  .diff-line { display: block; padding: 0 0.4em; }
  .diff-add { background: color-mix(in srgb, var(--ok) 14%, transparent); color: var(--ok); }
  .diff-del { background: color-mix(in srgb, var(--danger) 14%, transparent); color: var(--danger); }
  .diff-meta { color: var(--mute); }
</style>
