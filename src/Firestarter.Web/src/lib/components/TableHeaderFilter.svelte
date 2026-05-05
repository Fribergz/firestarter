<script lang="ts" module>
  export type SortDir = 'asc' | 'desc' | null;

  export type FilterState =
    | { kind: 'none' }
    | { kind: 'text'; value: string }
    | { kind: 'set'; selected: string[] }
    | { kind: 'number'; op: 'eq' | 'lt' | 'gt'; value: number | null }
    | {
        kind: 'date';
        op: 'before' | 'after' | 'on' | 'between';
        from: string | null;
        to: string | null;
      };

  export function isFilterActive(f: FilterState): boolean {
    switch (f.kind) {
      case 'text':
        return f.value.trim() !== '';
      case 'set':
        return f.selected.length > 0;
      case 'number':
        return f.value !== null && !Number.isNaN(f.value);
      case 'date':
        return !!f.from || !!f.to;
      default:
        return false;
    }
  }
</script>

<script lang="ts">
  interface Props {
    label: string;
    align?: 'left' | 'right';
    kind: 'text' | 'set' | 'number' | 'date';
    sortDir: SortDir;
    onSort: () => void;
    filter: FilterState;
    onFilterChange: (f: FilterState) => void;
    options?: string[];
    thClass?: string;
    thStyle?: string;
    onOpenChange?: (open: boolean) => void;
    filterable?: boolean;
    sortable?: boolean;
  }

  let {
    label,
    align = 'left',
    kind,
    sortDir,
    onSort,
    filter,
    onFilterChange,
    options = [],
    thClass = '',
    thStyle = '',
    onOpenChange,
    filterable = true,
    sortable = true,
  }: Props = $props();

  let open = $state(false);
  $effect(() => {
    onOpenChange?.(open);
  });
  let btnEl = $state<HTMLButtonElement | null>(null);
  let popEl = $state<HTMLDivElement | null>(null);
  let popTop = $state(0);
  let popLeft = $state<number | null>(0);
  let popRight = $state<number | null>(null);

  function portal(node: HTMLElement) {
    document.body.appendChild(node);
    return {
      destroy() {
        if (node.parentNode) node.parentNode.removeChild(node);
      },
    };
  }

  const active = $derived(isFilterActive(filter));

  const popMinWidth = 220;
  const viewportPad = 8;

  function position() {
    if (!btnEl) return;
    const r = btnEl.getBoundingClientRect();
    popTop = r.bottom + 4;
    // Anchor on the side the column is aligned to, but flip if that anchor
    // would push the popout outside the viewport.
    const wantRight = align === 'right';
    const wouldOverflowRight = r.left + popMinWidth > window.innerWidth - viewportPad;
    const wouldOverflowLeft = r.right - popMinWidth < viewportPad;
    const useRightAnchor = wantRight ? !wouldOverflowLeft : wouldOverflowRight;
    if (useRightAnchor) {
      popLeft = null;
      popRight = Math.max(viewportPad, window.innerWidth - r.right);
    } else {
      popRight = null;
      popLeft = Math.max(viewportPad, r.left);
    }
  }

  function openPop(e: MouseEvent) {
    e.stopPropagation();
    position();
    open = true;
  }

  function close() {
    open = false;
  }

  function onDocPointer(e: PointerEvent) {
    const t = e.target as Node | null;
    if (popEl && t && popEl.contains(t)) return;
    if (btnEl && t && btnEl.contains(t)) return;
    close();
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') close();
  }

  function onScroll(e: Event) {
    // Ignore scrolls happening inside the popout itself (e.g. the multi-select list)
    // — only close when an outer/page-level scroll moves the anchor away.
    const t = e.target as Node | null;
    if (popEl && t && popEl.contains(t)) return;
    close();
  }

  $effect(() => {
    if (!open) return;
    document.addEventListener('pointerdown', onDocPointer, true);
    document.addEventListener('keydown', onKey);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', onScroll);
    return () => {
      document.removeEventListener('pointerdown', onDocPointer, true);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', onScroll);
    };
  });

  function clear() {
    onFilterChange({ kind: 'none' });
  }

  function toggleSet(v: string) {
    const current = filter.kind === 'set' ? filter.selected : [];
    const next = current.includes(v) ? current.filter((x) => x !== v) : [...current, v];
    onFilterChange({ kind: 'set', selected: next });
  }

  const numF = $derived.by(() =>
    filter.kind === 'number' ? filter : { kind: 'number' as const, op: 'eq' as const, value: null },
  );
  const dateF = $derived.by(() =>
    filter.kind === 'date'
      ? filter
      : { kind: 'date' as const, op: 'after' as const, from: null, to: null },
  );
</script>

<th class={thClass} style={thStyle}>
  <span class="flex w-full items-center gap-1" style="line-height:1">
    {#if !sortable && filterable}
      <!-- Combined mode: the label itself is the filter trigger (no separate funnel icon). -->
      <button
        bind:this={btnEl}
        type="button"
        onclick={openPop}
        aria-label="Filter {label}"
        aria-expanded={open}
        class="cursor-pointer transition-colors hover:text-ink"
        class:ml-auto={align === 'right'}
        class:text-accent={active || open}
        style="font:inherit; color:inherit; letter-spacing:inherit; text-transform:inherit; background:none; border:0; padding:0"
      >{label}</button>
    {:else}
      {#if sortable}
        <button
          type="button"
          onclick={onSort}
          class="cursor-pointer transition-colors hover:text-ink"
          class:ml-auto={align === 'right'}
          style="font:inherit; color:inherit; letter-spacing:inherit; text-transform:inherit; background:none; border:0; padding:0"
        >
          {label}{#if sortDir === 'asc'}<span aria-hidden="true">&nbsp;↑</span>{:else if sortDir === 'desc'}<span aria-hidden="true">&nbsp;↓</span>{/if}
        </button>
      {:else}
        <span class:ml-auto={align === 'right'}>{label}</span>
      {/if}
      {#if filterable}
      <button
        bind:this={btnEl}
        type="button"
        onclick={openPop}
        aria-label="Filter {label}"
        aria-expanded={open}
        class="ml-auto inline-flex cursor-pointer items-center justify-center pl-1 leading-none transition-colors hover:text-ink"
        class:text-accent={active || open}
        style="background:none; border:0"
      >
        <svg
          class="block"
          width="11"
          height="11"
          viewBox="0 0 16 16"
          fill="none"
          stroke="currentColor"
          stroke-width="1.4"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M2.5 3h11l-4.2 5.2v4.3l-2.6 1.3V8.2L2.5 3z" fill={active ? 'currentColor' : 'none'} />
        </svg>
      </button>
      {/if}
    {/if}
  </span>
</th>

{#if open && filterable}
  <div
    bind:this={popEl}
    use:portal
    role="dialog"
    class="fixed z-50 border border-hair bg-paper p-3 text-[12px] text-ink shadow-[var(--shadow)]"
    style="top:{popTop}px; {popLeft !== null ? `left:${popLeft}px` : `right:${popRight}px`}; min-width:220px; border-radius:3px; border-width:0.5px; font-family:var(--font-sans)"
  >
    {#if kind === 'text'}
      <input
        type="text"
        class="focus-ring w-full border border-hair bg-paper px-2 py-1 text-ink placeholder:text-mute"
        style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:12px"
        placeholder="contains…"
        value={filter.kind === 'text' ? filter.value : ''}
        oninput={(e) =>
          onFilterChange({ kind: 'text', value: (e.currentTarget as HTMLInputElement).value })}
      />
    {:else if kind === 'set'}
      {#if options.length === 0}
        <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">no values</p>
      {:else}
        <div class="max-h-56 overflow-auto">
          {#each options as opt (opt)}
            {@const checked = filter.kind === 'set' && filter.selected.includes(opt)}
            <label class="flex cursor-pointer items-center gap-2 py-1 hover:text-ink">
              <input type="checkbox" {checked} onchange={() => toggleSet(opt)} />
              <span class="font-mono text-[11.5px]">{opt || '—'}</span>
            </label>
          {/each}
        </div>
      {/if}
    {:else if kind === 'number'}
      {@const numOps = [
        { v: 'eq', label: '=', title: 'equal to' },
        { v: 'lt', label: '<', title: 'lower than' },
        { v: 'gt', label: '>', title: 'higher than' },
      ] as const}
      <div class="flex flex-col gap-2">
        <div class="seg-group">
          {#each numOps as op (op.v)}
            <button
              type="button"
              class="seg"
              class:seg-active={numF.op === op.v}
              title={op.title}
              onclick={() =>
                onFilterChange({ kind: 'number', op: op.v, value: numF.value })}
            >{op.label}</button>
          {/each}
        </div>
        <input
          type="number"
          class="focus-ring border border-hair bg-paper px-2 py-1 text-ink"
          style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:12px"
          value={numF.value ?? ''}
          oninput={(e) => {
            const v = (e.currentTarget as HTMLInputElement).value;
            onFilterChange({ kind: 'number', op: numF.op, value: v === '' ? null : Number(v) });
          }}
        />
      </div>
    {:else if kind === 'date'}
      {@const dateOps = [
        { v: 'before', label: 'before' },
        { v: 'after', label: 'after' },
        { v: 'on', label: 'on' },
        { v: 'between', label: 'between' },
      ] as const}
      <div class="flex flex-col gap-2">
        <div class="seg-group">
          {#each dateOps as op (op.v)}
            <button
              type="button"
              class="seg"
              class:seg-active={dateF.op === op.v}
              onclick={() =>
                onFilterChange({
                  kind: 'date',
                  op: op.v,
                  from: dateF.from,
                  to: dateF.to,
                })}
            >{op.label}</button>
          {/each}
        </div>
        <input
          type="date"
          class="focus-ring border border-hair bg-paper px-2 py-1 text-ink"
          style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:12px"
          value={dateF.from ?? ''}
          oninput={(e) =>
            onFilterChange({
              kind: 'date',
              op: dateF.op,
              from: (e.currentTarget as HTMLInputElement).value || null,
              to: dateF.to,
            })}
        />
        {#if dateF.op === 'between'}
          <input
            type="date"
            class="focus-ring border border-hair bg-paper px-2 py-1 text-ink"
            style="border-radius:var(--radius); border-width:0.5px; font-family:var(--font-mono); font-size:12px"
            value={dateF.to ?? ''}
            oninput={(e) =>
              onFilterChange({
                kind: 'date',
                op: dateF.op,
                from: dateF.from,
                to: (e.currentTarget as HTMLInputElement).value || null,
              })}
          />
        {/if}
      </div>
    {/if}
    <div class="mt-3 flex items-center justify-between gap-2">
      <button
        type="button"
        onclick={clear}
        class="cursor-pointer text-mute hover:text-ink"
        style="font-family:var(--font-mono); font-size:11px; background:none; border:0; padding:0"
      >clear</button>
      <button
        type="button"
        onclick={close}
        class="cursor-pointer text-ink"
        style="font-family:var(--font-mono); font-size:11px; background:none; border:0; padding:0"
      >done</button>
    </div>
  </div>
{/if}

<style>
  .seg-group {
    display: inline-flex;
    border: 0.5px solid var(--hair);
    border-radius: var(--radius);
    overflow: hidden;
    background: var(--paper);
  }
  .seg {
    flex: 1;
    cursor: pointer;
    background: transparent;
    border: 0;
    padding: 4px 8px;
    font-family: var(--font-mono);
    font-size: 11.5px;
    color: var(--subink);
    transition: background-color 120ms, color 120ms;
  }
  .seg + .seg {
    border-left: 0.5px solid var(--hair);
  }
  .seg:hover {
    color: var(--ink);
    background: var(--panel-2);
  }
  .seg-active,
  .seg-active:hover {
    background: var(--ink);
    color: var(--paper);
  }
</style>
