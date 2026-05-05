<script lang="ts">
  import { onMount } from 'svelte';
  import {
    api,
    type ExtensionView,
    type ExtensionScanResult,
  } from '../lib/api';
  import { theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';

  let root = $state<string | null>(null);
  let rootInput = $state('');
  let rootSaving = $state(false);
  let rootError = $state<string | null>(null);

  let scanning = $state(false);
  let scanResult = $state<ExtensionScanResult | null>(null);
  let scanError = $state<string | null>(null);

  let extensions = $state<ExtensionView[]>([]);
  let listError = $state<string | null>(null);
  let togglingId = $state<number | null>(null);
  let toggleError = $state<string | null>(null);
  let openSettingsId = $state<number | null>(null);
  let savingSettingsId = $state<number | null>(null);
  let settingsError = $state<Record<number, string | null>>({});
  /** Per-extension working draft, keyed by extension id then setting name. */
  let drafts = $state<Record<number, Record<string, string>>>({});

  onMount(async () => {
    await Promise.all([loadRoot(), loadExtensions()]);
  });

  async function loadRoot() {
    try {
      const r = await api.getExtensionsRoot();
      root = r.root;
      rootInput = r.root ?? '';
      rootError = null;
    } catch (err) {
      rootError = err instanceof Error ? err.message : String(err);
    }
  }

  async function saveRoot() {
    rootSaving = true;
    rootError = null;
    try {
      const r = await api.setExtensionsRoot(rootInput.trim() === '' ? null : rootInput.trim());
      root = r.root;
      rootInput = r.root ?? '';
    } catch (err) {
      rootError = err instanceof Error ? err.message : String(err);
    } finally {
      rootSaving = false;
    }
  }

  async function scan() {
    scanning = true;
    scanError = null;
    scanResult = null;
    try {
      scanResult = await api.scanExtensions();
      await loadExtensions();
    } catch (err) {
      scanError = err instanceof Error ? err.message : String(err);
    } finally {
      scanning = false;
    }
  }

  async function loadExtensions() {
    try {
      const res = await api.listExtensions();
      extensions = res.extensions;
      listError = null;
    } catch (err) {
      listError = err instanceof Error ? err.message : String(err);
    }
  }

  async function toggle(ext: ExtensionView) {
    togglingId = ext.id;
    toggleError = null;
    try {
      const res = await api.setExtensionEnabled(ext.id, !ext.isEnabled);
      extensions = extensions.map(e => e.id === ext.id ? res.extension : e);
    } catch (err) {
      toggleError = err instanceof Error ? err.message : String(err);
    } finally {
      togglingId = null;
    }
  }

  function targetLabel(view: string): string {
    switch (view) {
      case 'merge-request': return 'MR';
      case 'project': return 'Project';
      default: return view;
    }
  }

  function openSettings(ext: ExtensionView) {
    if (openSettingsId === ext.id) {
      openSettingsId = null;
      return;
    }
    drafts = {
      ...drafts,
      [ext.id]: Object.fromEntries(
        ext.settingsSchema.map(s => [s.name, ext.settingsValues[s.name] ?? s.default ?? '']),
      ),
    };
    openSettingsId = ext.id;
  }

  function setDraft(extId: number, name: string, value: string) {
    drafts = { ...drafts, [extId]: { ...(drafts[extId] ?? {}), [name]: value } };
  }

  async function saveSettings(ext: ExtensionView) {
    savingSettingsId = ext.id;
    settingsError = { ...settingsError, [ext.id]: null };
    try {
      const draft = drafts[ext.id] ?? {};
      const res = await api.setExtensionSettings(ext.id, draft);
      extensions = extensions.map(e => e.id === ext.id ? res.extension : e);
      drafts = {
        ...drafts,
        [ext.id]: Object.fromEntries(
          res.extension.settingsSchema.map(s => [s.name, res.extension.settingsValues[s.name] ?? s.default ?? '']),
        ),
      };
    } catch (err) {
      settingsError = { ...settingsError, [ext.id]: err instanceof Error ? err.message : String(err) };
    } finally {
      savingSettingsId = null;
    }
  }

  function settingLabel(s: { name: string; label: string | null }): string {
    return s.label && s.label.trim() !== '' ? s.label : s.name;
  }

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  const inputStyle = 'font-family:var(--font-mono); font-size:12px; border-radius:var(--radius)';
  const chrome = $derived(theme.chrome);
</script>

<div class="flex min-h-0 flex-1 flex-col">
  <PageHeader title={chrome.sectionLabelCasing === 'sentence' ? 'Extensions' : 'EXTENSIONS'} />

  <div class="min-h-0 flex-1 overflow-auto bg-bg p-6">
    <div class="mx-auto max-w-5xl space-y-4">
      <section class="border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
        <h2 class="mb-3" style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Extensions root' : 'EXTENSIONS ROOT'}</h2>
        <div class="flex gap-2">
          <input
            type="text"
            bind:value={rootInput}
            placeholder="C:\path\to\extensions"
            class="focus-ring flex-1 border border-hair-strong bg-paper px-3 py-1.5 text-ink placeholder:text-mute"
            style={inputStyle}
          />
          <button
            type="button"
            onclick={saveRoot}
            disabled={rootSaving}
            class="border border-accent px-3 py-1.5 text-[12px] disabled:opacity-50"
            style="border-radius:var(--radius); color:var(--paper); background:var(--accent)"
          >
            Save
          </button>
          <button
            type="button"
            onclick={scan}
            disabled={scanning || !root}
            class="border border-hair-strong bg-paper px-3 py-1.5 text-[12px] text-ink hover:bg-panel-2 disabled:opacity-50"
            style="border-radius:var(--radius)"
          >
            {scanning ? 'Scanning…' : 'Scan'}
          </button>
        </div>
        {#if rootError}<p class="mt-2 text-danger" style="font-size:12.5px">{rootError}</p>{/if}
        {#if scanError}<p class="mt-2 text-danger" style="font-size:12.5px">{scanError}</p>{/if}
        {#if scanResult}
          <p class="mt-2 text-mute" style="font-family:var(--font-mono); font-size:11px">
            added {scanResult.added} · updated {scanResult.updated} · removed {scanResult.removed}
          </p>
          {#if scanResult.errors.length > 0}
            <ul class="mt-2 space-y-1 border border-danger p-2 text-danger"
                style="border-radius:var(--radius); background:var(--danger-soft); font-family:var(--font-mono); font-size:11px">
              {#each scanResult.errors as e}
                <li>{e}</li>
              {/each}
            </ul>
          {/if}
        {/if}
      </section>

      <section class="border border-hair bg-paper" style="border-radius:var(--radius-lg)">
        <div class="flex items-center justify-between border-b border-hair px-5 py-3">
          <h2 style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Installed extensions' : 'INSTALLED EXTENSIONS'}</h2>
          <button
            type="button"
            onclick={loadExtensions}
            class="text-mute hover:text-ink"
            style="font-family:var(--font-mono); font-size:11px"
          >
            refresh
          </button>
        </div>
        {#if toggleError}
          <p class="px-5 pt-3 text-danger" style="font-size:12.5px">{toggleError}</p>
        {/if}
        {#if listError}
          <p class="p-5 text-danger" style="font-size:12.5px">{listError}</p>
        {:else if extensions.length === 0}
          <p class="p-5 text-mute" style="font-size:12px">No extensions. Set the root and scan.</p>
        {:else}
          <ul class="divide-y divide-hair">
            {#each extensions as ext (ext.id)}
              {@const draft = drafts[ext.id] ?? {}}
              {@const open = openSettingsId === ext.id}
              <li class="px-5 py-4">
                <div class="flex items-start justify-between gap-4">
                  <div class="min-w-0 flex-1 space-y-1">
                    <div class="flex items-center gap-2">
                      <span class="text-ink" style="font-size:13px; font-weight:500">{ext.name}</span>
                      {#each ext.targets as t (t.view)}
                        <span class="border border-hair-strong px-2 py-0.5 text-mute"
                              style="border-radius:var(--radius); font-family:var(--font-mono); font-size:10.5px">
                          {targetLabel(t.view)}{t.label && t.label !== ext.name ? ` · ${t.label}` : ''}
                        </span>
                      {/each}
                    </div>
                    {#if ext.description}
                      <p class="text-subink" style="font-size:12.5px">{ext.description}</p>
                    {/if}
                    <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">
                      {ext.scriptPath} · timeout {ext.timeoutSeconds}s
                    </p>
                  </div>
                  <div class="flex shrink-0 items-center gap-2">
                    {#if ext.settingsSchema.length > 0}
                      <button
                        type="button"
                        onclick={() => openSettings(ext)}
                        class="border border-hair-strong bg-paper px-3 py-1.5 text-[12px] text-ink hover:bg-panel-2"
                        style="border-radius:var(--radius)"
                      >
                        {open ? 'Close settings' : 'Settings'}
                      </button>
                    {/if}
                    <button
                      type="button"
                      onclick={() => toggle(ext)}
                      disabled={togglingId === ext.id}
                      class="border px-3 py-1.5 text-[12px] disabled:opacity-50"
                      class:border-accent={ext.isEnabled}
                      class:border-hair-strong={!ext.isEnabled}
                      style={`border-radius:var(--radius); ${ext.isEnabled ? 'color:var(--paper); background:var(--accent)' : 'color:var(--ink); background:var(--paper)'}`}
                    >
                      {ext.isEnabled ? 'Enabled' : 'Disabled'}
                    </button>
                  </div>
                </div>

                {#if open}
                  <div class="mt-3 border border-hair bg-panel-2 p-3" style="border-radius:var(--radius)">
                    <div class="space-y-3">
                      {#each ext.settingsSchema as s (s.name)}
                        <div class="space-y-1">
                          <label class="block text-ink" style="font-size:12px; font-weight:500" for={`set-${ext.id}-${s.name}`}>
                            {settingLabel(s)}
                          </label>
                          {#if s.description}
                            <p class="text-mute" style="font-size:11.5px">{s.description}</p>
                          {/if}
                          {#if s.type === 'boolean'}
                            <label class="flex items-center gap-2 text-subink" style="font-size:12.5px" for={`set-${ext.id}-${s.name}`}>
                              <input
                                id={`set-${ext.id}-${s.name}`}
                                type="checkbox"
                                checked={(draft[s.name] ?? '').toLowerCase() === 'true'}
                                onchange={(e) => setDraft(ext.id, s.name, (e.currentTarget as HTMLInputElement).checked ? 'true' : 'false')}
                              />
                              <span style="font-family:var(--font-mono); font-size:11.5px">{(draft[s.name] ?? '').toLowerCase() === 'true' ? 'true' : 'false'}</span>
                            </label>
                          {:else if s.type === 'multi-string'}
                            <input
                              id={`set-${ext.id}-${s.name}`}
                              type="text"
                              placeholder="comma-separated, e.g. CA1822,IDE0051,WHITESPACE"
                              value={draft[s.name] ?? ''}
                              oninput={(e) => setDraft(ext.id, s.name, (e.currentTarget as HTMLInputElement).value)}
                              class="focus-ring w-full border border-hair-strong bg-paper px-3 py-1.5 text-ink placeholder:text-mute"
                              style={inputStyle}
                            />
                          {:else}
                            <input
                              id={`set-${ext.id}-${s.name}`}
                              type="text"
                              value={draft[s.name] ?? ''}
                              oninput={(e) => setDraft(ext.id, s.name, (e.currentTarget as HTMLInputElement).value)}
                              class="focus-ring w-full border border-hair-strong bg-paper px-3 py-1.5 text-ink placeholder:text-mute"
                              style={inputStyle}
                            />
                          {/if}
                        </div>
                      {/each}
                      <div class="flex items-center gap-2">
                        <button
                          type="button"
                          onclick={() => saveSettings(ext)}
                          disabled={savingSettingsId === ext.id}
                          class="border border-accent px-3 py-1.5 text-[12px] disabled:opacity-50"
                          style="border-radius:var(--radius); color:var(--paper); background:var(--accent)"
                        >
                          {savingSettingsId === ext.id ? 'Saving…' : 'Save settings'}
                        </button>
                        {#if settingsError[ext.id]}
                          <span class="text-danger" style="font-size:12px">{settingsError[ext.id]}</span>
                        {/if}
                      </div>
                    </div>
                  </div>
                {/if}
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    </div>
  </div>
</div>
