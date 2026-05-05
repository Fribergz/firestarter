<script lang="ts">
  import type { JenkinsPipelineItem } from '../api';
  import Pill from './Pill.svelte';
  import { timeAgo } from '../format';

  interface Props {
    pipelines: JenkinsPipelineItem[];
    jenkinsConfigured: boolean;
    error: string | null;
    /** Section heading (e.g. sentence vs uppercase from chrome) */
    title?: string;
    /** When false, parent supplies the title row (e.g. with “View all”) */
    includeHeading?: boolean;
    /** Project overview: one line per build, no step pills; failure text inline after SHA/— */
    condensed?: boolean;
  }

  let {
    pipelines,
    jenkinsConfigured,
    error,
    title = 'Pipelines',
    includeHeading = true,
    condensed = false,
  }: Props = $props();

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';

  function pipelineStatusTone(
    status: string,
  ): 'passed' | 'failed' | 'running' | 'queued' | 'draft' | 'neutral' {
    const s = status.toLowerCase();
    if (s === 'success') return 'passed';
    if (s === 'failure' || s === 'failed' || s === 'unstable') return 'failed';
    if (s === 'running' || s === 'pending') return 'running';
    if (s === 'aborted' || s === 'not_built') return 'neutral';
    return 'neutral';
  }

  function stepStatusTone(
    status: string,
  ): 'passed' | 'failed' | 'running' | 'queued' | 'draft' | 'neutral' {
    const s = status.toLowerCase();
    if (s === 'success' || s === 'passed') return 'passed';
    if (s === 'failure' || s === 'failed') return 'failed';
    if (s === 'unstable') return 'queued';
    if (s === 'aborted' || s === 'not_built') return 'neutral';
    if (s === 'skipped') return 'neutral';
    if (s === 'running' || s === 'in_progress' || s === 'pending') return 'running';
    return 'neutral';
  }
</script>

{#if includeHeading}
  <div class="mb-3">
    <h2 style={labelFont}>{title}</h2>
  </div>
{/if}
{#if error}
  <p class="text-danger" style="font-size:12px">{error}</p>
{:else if !jenkinsConfigured}
  <p class="text-mute" style="font-size:12px">
    Configure Jenkins in Settings and set this project’s Jenkins job path to load recent builds.
  </p>
{:else if pipelines.length === 0}
  <p class="text-mute" style="font-size:12px">No recent Jenkins builds for this job.</p>
{:else}
  <ul class="divide-y divide-hair">
    {#each pipelines as pipe (pipe.number)}
      <li class="space-y-2 py-3">
        <div class="flex flex-wrap items-center gap-2">
          <span class="text-mute" style="font-family:var(--font-mono); font-size:10.5px">#{pipe.number}</span>
          <Pill tone={pipelineStatusTone(pipe.status)}>{pipe.status}</Pill>
          {#if pipe.versionTag}
            <Pill tone="open">{pipe.versionTag}</Pill>
          {/if}
          <span
            class="min-w-0 flex-1 truncate text-subink"
            style="font-family:var(--font-mono); font-size:11.5px"
            title={pipe.sha ?? pipe.failedOn ?? ''}
          >
            {#if pipe.sha}
              {pipe.sha.slice(0, 8)}
            {:else}
              —
            {/if}
            {#if condensed && pipe.failedOn}
              <span class="text-danger" style="font-size:10.5px; font-family:var(--font-sans)">
                · Failed / aborted on: <span style="font-family:var(--font-mono)">{pipe.failedOn}</span>
              </span>
            {/if}
          </span>
          <span class="text-mute" style="font-family:var(--font-mono); font-size:10.5px">
            {timeAgo(pipe.startedAt ?? '')}
          </span>
          {#if pipe.url}
            <a
              href={pipe.url}
              target="_blank"
              rel="noreferrer"
              class="shrink-0 text-accent"
              style="font-family:var(--font-mono); font-size:10.5px"
            >build ↗</a>
          {/if}
        </div>
        {#if pipe.failedOn && !condensed}
          <p class="text-danger" style="font-size:11px; line-height:1.4">
            Failed / aborted on:
            <span style="font-family:var(--font-mono)">{pipe.failedOn}</span>
          </p>
        {/if}
        {#if !condensed && pipe.steps && pipe.steps.length > 0}
          <div class="flex flex-wrap gap-1">
            {#each pipe.steps as st, si (`${pipe.number}-${si}-${st.name}`)}
              <Pill tone={stepStatusTone(st.status)}>
                <span class="max-w-[11rem] truncate" style="font-family:var(--font-mono); font-size:10px" title="{st.name} — {st.status}">
                  {st.name}
                </span>
              </Pill>
            {/each}
          </div>
        {/if}
      </li>
    {/each}
  </ul>
{/if}
