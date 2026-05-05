<script lang="ts">
  import { windowStartResize, type ResizeEdge } from '../ipc';

  const edges: { edge: ResizeEdge; cls: string; style: string }[] = [
    { edge: 'top',          cls: 'cursor-ns-resize', style: 'top:0;left:6px;right:6px;height:4px;' },
    { edge: 'bottom',       cls: 'cursor-ns-resize', style: 'bottom:0;left:6px;right:6px;height:4px;' },
    { edge: 'left',         cls: 'cursor-ew-resize', style: 'top:6px;bottom:6px;left:0;width:4px;' },
    { edge: 'right',        cls: 'cursor-ew-resize', style: 'top:6px;bottom:6px;right:0;width:4px;' },
    { edge: 'top-left',     cls: 'cursor-nwse-resize', style: 'top:0;left:0;width:6px;height:6px;' },
    { edge: 'top-right',    cls: 'cursor-nesw-resize', style: 'top:0;right:0;width:6px;height:6px;' },
    { edge: 'bottom-left',  cls: 'cursor-nesw-resize', style: 'bottom:0;left:0;width:6px;height:6px;' },
    { edge: 'bottom-right', cls: 'cursor-nwse-resize', style: 'bottom:0;right:0;width:6px;height:6px;' },
  ];

  function onPointerDown(e: PointerEvent, edge: ResizeEdge) {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    windowStartResize(edge).catch(() => {});
  }
</script>

<div class="pointer-events-none fixed inset-0 z-50">
  {#each edges as h (h.edge)}
    <div
      class={`pointer-events-auto absolute ${h.cls}`}
      style={h.style}
      onpointerdown={(e) => onPointerDown(e, h.edge)}
    ></div>
  {/each}
</div>
