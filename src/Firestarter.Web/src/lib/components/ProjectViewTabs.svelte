<script lang="ts">
  import type { UiChrome } from '../theme/chrome';

  export type ProjectTab = { id: string; label: string };

  interface Props {
    tabs: ProjectTab[];
    activeId: string;
    onSelect: (id: string) => void;
    /** From `theme.chrome` for label casing. */
    chrome: UiChrome;
  }

  let { tabs, activeId, onSelect, chrome }: Props = $props();
</script>

<div class="flex items-center gap-5 border-b border-hair bg-paper px-6">
  {#each tabs as t (t.id)}
    {@const active = t.id === activeId}
    <button
      type="button"
      onclick={() => onSelect(t.id)}
      class="relative py-3 text-[12px]"
      class:text-ink={active}
      class:text-subink={!active}
      style={active ? 'font-weight:500' : ''}
    >
      {chrome.sectionLabelCasing === 'sentence' ? t.label : t.label.toUpperCase()}
      {#if active}<span class="absolute inset-x-0 bottom-0 h-[2px] bg-accent"></span>{/if}
    </button>
  {/each}
</div>
