<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../api';
  import { route } from '../stores/route.svelte';

  let reviewerBadge = $state(0);

  onMount(async () => {
    try {
      const c = await api.getCounters();
      reviewerBadge = c.reviewerOpen;
    } catch {
      /* bridge / offline */
    }
  });

  const activeNav = $derived.by(() => {
    const n = route.current.name;
    if (n === 'start') return 'start';
    if (n === 'projects' || n === 'project' || n === 'project-pipelines' || n === 'mr') return 'projects';
    if (n === 'mr-mine') return 'mr-mine';
    if (n === 'mr-reviewer') return 'mr-reviewer';
    if (n === 'pipelines-cache') return 'pipelines';
    return null;
  });

</script>

<aside
  class="flex w-[200px] shrink-0 flex-col border-r border-hair bg-paper py-[18px] pl-2.5 pr-2.5"
  style="border-right-width:0.5px"
  aria-label="Navigate"
>
  <div class="px-2 pb-2.5 font-mono text-[10px] uppercase tracking-[0.14em] text-subink">
    Navigate
  </div>

  <nav class="flex flex-col gap-0.5">
    <button
      type="button"
      class="flex w-full items-center gap-2.5 rounded-[5px] px-2.5 py-[7px] text-left text-[12.5px] transition-colors"
      class:text-accent={activeNav === 'start'}
      class:bg-[rgba(201,71,31,0.07)]={activeNav === 'start'}
      class:text-ink={activeNav !== 'start'}
      class:hover:bg-panel-2={activeNav !== 'start'}
      onclick={() => route.goStart()}
    >
      <span class="w-3.5 shrink-0 text-center text-[11px]" aria-hidden="true">◆</span>
      <span class="min-w-0 flex-1 truncate">Start</span>
    </button>

    <button
      type="button"
      class="flex w-full items-center gap-2.5 rounded-[5px] px-2.5 py-[7px] text-left text-[12.5px] transition-colors"
      class:text-accent={activeNav === 'projects'}
      class:bg-[rgba(201,71,31,0.07)]={activeNav === 'projects'}
      class:text-ink={activeNav !== 'projects'}
      class:hover:bg-panel-2={activeNav !== 'projects'}
      onclick={() => route.goProjects()}
    >
      <span class="w-3.5 shrink-0 text-center text-[11px]" aria-hidden="true">▤</span>
      <span class="min-w-0 flex-1 truncate">Projects</span>
    </button>

    <button
      type="button"
      class="flex w-full items-center gap-2.5 rounded-[5px] px-2.5 py-[7px] text-left text-[12.5px] transition-colors"
      class:text-accent={activeNav === 'mr-mine'}
      class:bg-[rgba(201,71,31,0.07)]={activeNav === 'mr-mine'}
      class:text-ink={activeNav !== 'mr-mine'}
      class:hover:bg-panel-2={activeNav !== 'mr-mine'}
      onclick={() => route.goMrMine()}
    >
      <span class="w-3.5 shrink-0 text-center text-[11px]" aria-hidden="true">⇌</span>
      <span class="min-w-0 flex-1 truncate">Authored</span>
    </button>

    <button
      type="button"
      class="flex w-full items-center gap-2.5 rounded-[5px] px-2.5 py-[7px] text-left text-[12.5px] transition-colors"
      class:text-accent={activeNav === 'mr-reviewer'}
      class:bg-[rgba(201,71,31,0.07)]={activeNav === 'mr-reviewer'}
      class:text-ink={activeNav !== 'mr-reviewer'}
      class:hover:bg-panel-2={activeNav !== 'mr-reviewer'}
      onclick={() => route.goMrReviewer()}
    >
      <span class="w-3.5 shrink-0 text-center text-[11px]" aria-hidden="true">◇</span>
      <span class="min-w-0 flex-1 truncate">Reviewing</span>
      {#if reviewerBadge > 0}
        <span
          class="shrink-0 rounded-full bg-accent px-1.5 py-px font-mono text-[10px] font-medium text-paper"
        >{reviewerBadge}</span>
      {/if}
    </button>

    <button
      type="button"
      class="flex w-full items-center gap-2.5 rounded-[5px] px-2.5 py-[7px] text-left text-[12.5px] transition-colors"
      class:text-accent={activeNav === 'pipelines'}
      class:bg-[rgba(201,71,31,0.07)]={activeNav === 'pipelines'}
      class:text-ink={activeNav !== 'pipelines'}
      class:hover:bg-panel-2={activeNav !== 'pipelines'}
      onclick={() => route.goPipelinesCache()}
    >
      <span class="w-3.5 shrink-0 text-center text-[11px]" aria-hidden="true">○</span>
      <span class="min-w-0 flex-1 truncate">Pipelines</span>
    </button>
  </nav>
</aside>
