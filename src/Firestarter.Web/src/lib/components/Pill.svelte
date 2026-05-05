<script lang="ts">
  type Tone = 'passed' | 'failed' | 'running' | 'queued' | 'draft' | 'open' | 'neutral';

  interface Props {
    tone?: Tone;
    children?: import('svelte').Snippet;
  }

  let { tone = 'neutral', children }: Props = $props();
</script>

<span class="pill pill--{tone}">
  <span class="dot"></span>
  {@render children?.()}
</span>
