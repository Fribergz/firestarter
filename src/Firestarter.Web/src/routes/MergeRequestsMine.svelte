<script lang="ts">
  import { onMount } from 'svelte';
  import { api, type MergeRequestListItem } from '../lib/api';
  import { route } from '../lib/stores/route.svelte';
  import { theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';
  import NavRail from '../lib/components/NavRail.svelte';
  import Pill from '../lib/components/Pill.svelte';
  import { timeAgo, splitPath } from '../lib/format';

  let items = $state<MergeRequestListItem[]>([]);
  let loading = $state(true);
  let error = $state<string | null>(null);

  onMount(load);

  async function load() {
    loading = true;
    error = null;
    try {
      const res = await api.listMyMrs();
      items = res.items;
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      loading = false;
    }
  }

  const chrome = $derived(theme.chrome);
  const headerLabelStyle = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const editorialThStyle =
    'font-family:var(--font-mono); font-size:10.5px; font-weight:500; letter-spacing:.12em; color:var(--subink); text-transform:uppercase';
</script>

<div class="flex min-h-0 flex-1" class:bg-bg={chrome.navSidebar}>
  {#if chrome.navSidebar}
    <NavRail />
  {/if}
  <div class="flex min-h-0 min-w-0 flex-1 flex-col" class:bg-bg={chrome.editorialListShell}>
  <PageHeader
    title={chrome.sectionLabelCasing === 'sentence' ? 'Authored or assigned' : 'AUTHORED OR ASSIGNED'}
    headerClass={chrome.editorialListShell ? '!bg-bg !px-7 !pb-3.5 !pt-[18px] border-b-[0.5px]' : ''}
  >
    {#snippet right()}
      {#if !chrome.editorialListShell}
        <button
          type="button"
          onclick={() => route.goStart()}
          class="border border-hair-strong bg-paper px-3 py-1.5 text-[12px] text-ink hover:bg-panel-2"
          style="border-radius:var(--radius)"
        >
          ← Start
        </button>
      {/if}
    {/snippet}
  </PageHeader>

  <div class="min-h-0 flex-1 overflow-auto" class:bg-bg={chrome.editorialListShell}>
    {#if error}
      <p class="p-6 text-danger">{error}</p>
    {:else if loading}
      <p class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
    {:else if items.length === 0}
      <p class="p-6 text-mute" style="font-family:var(--font-mono); font-size:11px">
        No open merge requests where you are the author or an assignee.
      </p>
    {:else}
      <table
        class="w-full border-collapse"
        class:bg-transparent={chrome.editorialDataList}
        class:bg-paper={!chrome.editorialDataList}
        style={chrome.editorialDataList ? 'font-size:12.5px' : undefined}
      >
        <thead>
          <tr class="text-left" class:border-b={!chrome.editorialDataList} class:border-hair={!chrome.editorialDataList}>
            <th
              class="w-16 py-2"
              class:pl-6={!chrome.editorialDataList}
              class:pl-7={chrome.editorialDataList}
              class:py-2.5={chrome.editorialDataList}
              style={chrome.editorialDataList ? editorialThStyle : headerLabelStyle}
            >{chrome.editorialDataList ? 'Iid' : 'IID'}</th>
            <th class="py-2" class:py-2.5={chrome.editorialDataList} style={chrome.editorialDataList ? editorialThStyle : headerLabelStyle}>
              {chrome.editorialDataList ? 'Title' : 'TITLE'}
            </th>
            <th class="py-2" class:py-2.5={chrome.editorialDataList} style={chrome.editorialDataList ? editorialThStyle : headerLabelStyle}>
              {chrome.editorialDataList ? 'Project' : 'PROJECT'}
            </th>
            <th class="py-2" class:py-2.5={chrome.editorialDataList} style={chrome.editorialDataList ? editorialThStyle : headerLabelStyle}>
              {chrome.editorialDataList ? 'Branches' : 'BRANCHES'}
            </th>
            <th class="py-2" class:py-2.5={chrome.editorialDataList} style={chrome.editorialDataList ? editorialThStyle : headerLabelStyle}>
              {chrome.editorialDataList ? 'Role' : 'ROLE'}
            </th>
            <th
              class="py-2 text-right"
              class:py-2.5={chrome.editorialDataList}
              class:pr-6={!chrome.editorialDataList}
              class:pr-7={chrome.editorialDataList}
              style={chrome.editorialDataList ? editorialThStyle : headerLabelStyle}
            >
              {chrome.editorialDataList ? 'Updated' : 'UPDATED'}
            </th>
          </tr>
        </thead>
        <tbody>
          {#each items as mr (mr.id)}
            {@const parts = splitPath(mr.projectPath)}
            <tr
              class="group cursor-pointer transition-colors hover:bg-panel-2"
              class:border-b={!chrome.editorialDataList}
              class:border-hair={!chrome.editorialDataList}
              style={chrome.editorialDataList ? 'border-top:0.5px solid var(--hair)' : undefined}
              onclick={() => route.goMr(mr.projectId, mr.iid)}
            >
              <td
                class="py-2.5 text-mute"
                class:pl-6={!chrome.editorialDataList}
                class:pl-7={chrome.editorialDataList}
                class:py-[11px]={chrome.editorialDataList}
                style="font-family:var(--font-mono); font-size:11.5px"
              >!{mr.iid}</td>
              <td class="py-2.5" class:py-[11px]={chrome.editorialDataList}>
                <div class="flex items-center gap-2">
                  <span class="text-ink" style="font-size:13px">{mr.title}</span>
                  {#if mr.draft}<Pill tone="draft">draft</Pill>{/if}
                </div>
                <div class="mt-0.5 text-mute" style="font-family:var(--font-mono); font-size:10.5px">
                  {mr.authorUsername ?? '—'}
                </div>
              </td>
              <td class="py-2.5" class:py-[11px]={chrome.editorialDataList} style="font-family:var(--font-mono); font-size:12px">
                {#if chrome.editorialDataList}
                  <span class="text-subink">{parts.ns}</span><span class="text-ink" style="font-weight:500">{parts.name}</span>
                {:else}
                  <span class="text-mute">{parts.ns}</span><span class="text-ink">{parts.name}</span>
                {/if}
              </td>
              <td class="py-2.5 text-subink" class:py-[11px]={chrome.editorialDataList} style="font-family:var(--font-mono); font-size:11px">
                {mr.sourceBranch} → {mr.targetBranch}
              </td>
              <td class="py-2.5" class:py-[11px]={chrome.editorialDataList} style="font-family:var(--font-mono); font-size:11px">
                {#if mr.authorUsername}
                  <Pill tone="open">author</Pill>
                {:else}
                  <Pill tone="queued">assignee</Pill>
                {/if}
              </td>
              <td
                class="py-2.5 text-right text-mute"
                class:py-[11px]={chrome.editorialDataList}
                class:pr-6={!chrome.editorialDataList}
                class:pr-7={chrome.editorialDataList}
                style="font-family:var(--font-mono); font-size:11px"
              >
                {timeAgo(mr.updatedAt)}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </div>
  </div>
</div>
