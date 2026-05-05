<script lang="ts">
  import { theme } from '../stores/theme.svelte';

  interface Props {
    open?: boolean;
    title: string;
    /** Simple alert: one button. confirm: two buttons (primary + cancel). */
    variant?: 'alert' | 'confirm';
    primaryLabel?: string;
    cancelLabel?: string;
    onPrimary?: () => void;
    onCancel?: () => void;
    children?: import('svelte').Snippet;
    /** Optional content rendered at the bottom-left of the footer (e.g. secondary actions). */
    footerLeft?: import('svelte').Snippet;
  }

  let {
    open = $bindable(false),
    title,
    variant = 'alert',
    primaryLabel = 'OK',
    cancelLabel = 'Cancel',
    onPrimary,
    onCancel,
    children,
    footerLeft,
  }: Props = $props();

  const chrome = $derived(theme.chrome);

  let el = $state<HTMLDialogElement | null>(null);

  $effect(() => {
    const d = el;
    if (!d) return;
    if (open) {
      if (!d.open) d.showModal();
    } else if (d.open) {
      d.close();
    }
  });

  function dismiss() {
    open = false;
  }

  function onDialogClose() {
    open = false;
  }

  function onPrimaryClick() {
    onPrimary?.();
    dismiss();
  }

  function onCancelClick() {
    onCancel?.();
    dismiss();
  }

  function onOverlayPointerDown(e: PointerEvent) {
    if (e.currentTarget !== e.target) return;
    if (variant === 'confirm') onCancelClick();
    else dismiss();
  }
</script>

<dialog
  bind:this={el}
  class="m-0 h-full w-full max-w-full border-0 bg-transparent p-0 text-ink focus:outline-none"
  style="max-height: 100dvh"
  aria-labelledby="app-dialog-title"
  onclose={onDialogClose}
>
  <div
    class="flex min-h-full w-full items-center justify-center p-4 {!chrome.warmTitleBar ? 'bg-ink/35' : ''}"
    style={chrome.warmTitleBar ? 'background:rgba(26, 21, 18, 0.4); backdrop-filter:blur(10px)' : undefined}
    onpointerdown={onOverlayPointerDown}
    role="presentation"
  >
    <div
      class="w-full max-w-sm border border-hair bg-paper p-4 text-[12px] shadow-[var(--shadow)]"
      class:font-mono={!chrome.warmTitleBar}
      class:font-sans={chrome.warmTitleBar}
      style="border-radius:var(--radius); border-width:{chrome.warmTitleBar ? '0.5px' : '1px'}"
      onpointerdown={(e) => e.stopPropagation()}
      role="document"
    >
      <h2
        class="mb-2 text-[13px] font-medium leading-tight"
        class:tracking-[0.04em]={!chrome.warmTitleBar}
        class:font-sans={chrome.warmTitleBar}
        class:font-serif={chrome.warmTitleBar}
        id="app-dialog-title"
      >{title}</h2>
      <div class="mb-4 text-subink">
        {@render children?.()}
      </div>
      <div class="flex items-center gap-2">
        {#if footerLeft}
          <div class="flex items-center gap-2">{@render footerLeft()}</div>
        {/if}
        <div class="ml-auto flex gap-2">
          {#if variant === 'confirm'}
            <button
              type="button"
              class="rounded border border-hair bg-paper px-3 py-1.5 text-[12px] text-ink transition-colors hover:bg-panel-2"
              style="border-width:{chrome.warmTitleBar ? '0.5px' : '1px'}"
              onclick={onCancelClick}
            >{cancelLabel}</button>
            <button
              type="button"
              class="rounded border border-transparent bg-accent px-3 py-1.5 text-[12px] text-white transition-colors hover:opacity-90"
              onclick={onPrimaryClick}
            >{primaryLabel}</button>
          {:else}
            <button
              type="button"
              class="rounded border border-transparent bg-accent px-3 py-1.5 text-[12px] text-white transition-colors hover:opacity-90"
              onclick={dismiss}
            >{primaryLabel}</button>
          {/if}
        </div>
      </div>
    </div>
  </div>
</dialog>
