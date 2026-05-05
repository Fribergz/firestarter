<script lang="ts">
  import {
    breadcrumbRowInlineStyle,
    formatBreadcrumbSegment,
    primaryHeadingStyle,
  } from '../theme/chrome';
  import { theme } from '../stores/theme.svelte';
  import { route, type RouteName } from '../stores/route.svelte';

  /** Crumb after home: plain string, or segment + optional navigation (e.g. `projects` → list). */
  type PageCrumb = string | { segment: string; onNavigate?: () => void };

  interface Props {
    /** If omitted, crumbs are derived from the current route (projects, authored, reviewing, …). */
    crumbs?: PageCrumb[];
    title: string;
    right?: import('svelte').Snippet;
    /** Extra classes on the root `<header>` (e.g. editorial list-page padding). */
    headerClass?: string;
    /** Small spinner after the breadcrumb trail (e.g. background GitLab sync). */
    breadcrumbSyncing?: boolean;
  }

  let { crumbs: crumbsProp, title, right, headerClass = '', breadcrumbSyncing = false }: Props = $props();

  function crumbsForRoute(name: RouteName): string[] {
    switch (name) {
      case 'projects': return ['projects'];
      case 'sync': return ['sync'];
      case 'extensions': return ['extensions'];
      case 'settings': return ['settings'];
      case 'mr-mine': return ['authored'];
      case 'mr-reviewer': return ['reviewing'];
      case 'pipelines-cache': return ['pipelines'];
      case 'stats': return ['statistics'];
      case 'start':
      case 'project':
      case 'project-pipelines':
      case 'mr':
        return [];
    }
  }

  const chrome = $derived(theme.chrome);

  const effectiveCrumbs = $derived(
    crumbsProp !== undefined ? crumbsProp : crumbsForRoute(route.current.name),
  );

  function crumbKey(c: PageCrumb): string {
    return typeof c === 'string' ? c : c.segment;
  }

  const crumbStyle = $derived(breadcrumbRowInlineStyle(chrome));

  const titleStyle = $derived(primaryHeadingStyle(chrome, 'page'));

  /** Title block + `right` share a row; `align-items: flex-end` lines actions up with the title. */
  const headerTrailingAlignEnd = $derived(chrome.pageHeaderTrailingAlignWithTitle && right !== undefined);
</script>

<header
  class="flex justify-between border-b border-hair bg-paper px-6 pt-4 pb-3.5 {headerClass}"
  class:items-end={headerTrailingAlignEnd}
  class:items-start={!headerTrailingAlignEnd}
>
  <div class="min-w-0">
    <div class="flex flex-wrap items-center gap-x-1 gap-y-0.5 text-subink" style={crumbStyle}>
      <button
        type="button"
        onclick={() => route.goStart()}
        class="inline-flex shrink-0 rounded p-0.5 text-subink transition-colors hover:bg-panel-2 hover:text-ink"
        aria-label="Start"
        title="Start"
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />
          <polyline points="9 22 9 12 15 12 15 22" />
        </svg>
      </button>
      {#each effectiveCrumbs as c, i (i)}
        <span class="text-mute select-none" aria-hidden="true">›</span>
        {@const label = formatBreadcrumbSegment(chrome, crumbKey(c))}
        {#if typeof c === 'object' && c.onNavigate}
          <button
            type="button"
            onclick={c.onNavigate}
            class="rounded-sm transition-colors hover:bg-panel-2 hover:text-ink"
          >{label}</button>
        {:else}
          <span>{label}</span>
        {/if}
      {/each}
      {#if breadcrumbSyncing}
        <span
          class="ml-0.5 inline-flex shrink-0 text-mute"
          role="status"
          aria-label="Syncing from GitLab"
          title="Syncing from GitLab"
        >
          <svg
            class="size-[13px] animate-spin"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
            aria-hidden="true"
          >
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path
              class="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
            />
          </svg>
        </span>
      {/if}
    </div>
    <h1 class="mt-1 text-ink" style={titleStyle}>{title}</h1>
  </div>
  {#if right}
    <div class="flex items-center gap-2">{@render right()}</div>
  {/if}
</header>
