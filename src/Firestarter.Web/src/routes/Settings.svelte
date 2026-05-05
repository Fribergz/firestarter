<script lang="ts">
  import { onMount } from 'svelte';
  import {
    api,
    type GitlabConfig,
    type IdeRegistration,
    type JenkinsConfig,
    type WorkspaceConfig,
  } from '../lib/api';
  import { theme, type Theme } from '../lib/stores/theme.svelte';
  import PageHeader from '../lib/components/PageHeader.svelte';

  let loading = $state(true);
  /** After a successful load — avoids persisting during hydration or before first load. */
  let settingsReady = $state(false);
  /** Fingerprint of last applied GitLab + workspace fields (avoids persisting on load / no-op edits). */
  let lastPersistKey = $state('');
  let saving = $state(false);
  let error = $state<string | null>(null);
  let toast = $state<string | null>(null);

  let baseUrl = $state('');
  let pat = $state('');
  let clearPat = $state(false);
  let syncIntervalSeconds = $state(300);
  let hasPat = $state(false);
  let currentUsername = $state<string | null>(null);

  let jenkinsBaseUrl = $state('');
  let jenkinsUsername = $state('');
  let jenkinsApiToken = $state('');
  let jenkinsClearToken = $state(false);
  let jenkinsHasToken = $state(false);
  let jenkinsTesting = $state(false);
  let jenkinsTestNote = $state<string | null>(null);

  let projectsRoot = $state('');
  let tempRoot = $state('');

  let ides = $state<IdeRegistration[]>([]);
  let ideDraft = $state({ name: '', executablePath: '', argTemplate: '"{path}"', isDefault: false });
  let ideError = $state<string | null>(null);

  const chrome = $derived(theme.chrome);

  const themeSamples: { id: Theme; label: string; blurb: string }[] = [
    { id: 'ember', label: 'Ember', blurb: 'Warm editorial, serif hero.' },
    { id: 'graphite', label: 'Graphite', blurb: 'Swiss technical, grid-aligned.' },
    { id: 'obsidian', label: 'Obsidian', blurb: 'Dark IDE, lime accent.' },
  ];

  async function load() {
    loading = true;
    settingsReady = false;
    error = null;
    try {
      const [cfg, jenkinsCfg, workspace, ideResp] = await Promise.all([
        api.getSettings(),
        api.getJenkinsConfig(),
        api.getWorkspace(),
        api.listIdes(),
      ]);
      applyGitlab(cfg);
      applyJenkins(jenkinsCfg);
      projectsRoot = workspace.projectsRoot;
      tempRoot = workspace.tempRoot;
      ides = ideResp.ides;
      pat = '';
      clearPat = false;
      jenkinsApiToken = '';
      jenkinsClearToken = false;
      jenkinsTestNote = null;
      lastPersistKey = settingsFingerprint();
      settingsReady = true;
    } catch (e) {
      error = String(e);
      settingsReady = false;
    } finally {
      loading = false;
    }
  }

  function applyGitlab(cfg: GitlabConfig) {
    baseUrl = cfg.baseUrl ?? '';
    syncIntervalSeconds = cfg.syncIntervalSeconds;
    hasPat = cfg.hasPat;
    currentUsername = cfg.currentUsername;
  }

  function applyJenkins(cfg: JenkinsConfig) {
    jenkinsBaseUrl = cfg.baseUrl ?? '';
    jenkinsUsername = cfg.username ?? '';
    jenkinsHasToken = cfg.hasApiToken;
  }

  function settingsFingerprint(): string {
    return JSON.stringify({
      baseUrl: baseUrl.trim(),
      syncIntervalSeconds: Number(syncIntervalSeconds),
      projectsRoot,
      tempRoot,
      clearPat,
      patPending: pat.length > 0,
      jenkinsBaseUrl: jenkinsBaseUrl.trim(),
      jenkinsUsername: jenkinsUsername.trim(),
      jenkinsClearToken,
      jenkinsTokenPending: jenkinsApiToken.length > 0,
    });
  }

  async function persistSettings() {
    saving = true;
    error = null;
    try {
      const cfg = await api.updateSettings({
        baseUrl: baseUrl.trim() || null,
        pat: pat.length ? pat : null,
        clearPat,
        syncIntervalSeconds,
      });
      applyGitlab(cfg);
      pat = '';
      clearPat = false;

      const jenkinsSaved = await api.updateJenkins({
        baseUrl: jenkinsBaseUrl.trim() || null,
        username: jenkinsUsername.trim() || null,
        apiToken: jenkinsApiToken.length ? jenkinsApiToken : null,
        clearApiToken: jenkinsClearToken,
      });
      applyJenkins(jenkinsSaved);
      jenkinsApiToken = '';
      jenkinsClearToken = false;

      const workspace = await api.updateWorkspace({ projectsRoot, tempRoot } as WorkspaceConfig);
      projectsRoot = workspace.projectsRoot;
      tempRoot = workspace.tempRoot;

      lastPersistKey = settingsFingerprint();
      toast = 'Saved';
      setTimeout(() => (toast = null), 1500);
    } catch (e) {
      error = String(e);
    } finally {
      saving = false;
    }
  }

  $effect(() => {
    if (!settingsReady || loading) return;
    void baseUrl;
    void pat;
    void clearPat;
    void syncIntervalSeconds;
    void projectsRoot;
    void tempRoot;
    void jenkinsBaseUrl;
    void jenkinsUsername;
    void jenkinsApiToken;
    void jenkinsClearToken;

    const fp = settingsFingerprint();
    if (fp === lastPersistKey) return;

    const id = setTimeout(() => {
      if (settingsFingerprint() !== fp) return;
      void persistSettings();
    }, 450);
    return () => clearTimeout(id);
  });

  async function testJenkinsConnection() {
    jenkinsTesting = true;
    jenkinsTestNote = null;
    error = null;
    try {
      await api.testJenkins();
      jenkinsTestNote = 'Jenkins API reachable.';
    } catch (e) {
      jenkinsTestNote = String(e);
    } finally {
      jenkinsTesting = false;
    }
  }

  async function addIde() {
    ideError = null;
    try {
      const saved = await api.upsertIde({
        name: ideDraft.name.trim(),
        executablePath: ideDraft.executablePath.trim(),
        argTemplate: ideDraft.argTemplate.trim() || '"{path}"',
        isDefault: ideDraft.isDefault,
      });
      ides = [...ides.filter((i) => i.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name));
      if (saved.isDefault) {
        ides = ides.map((i) => ({ ...i, isDefault: i.id === saved.id }));
      }
      ideDraft = { name: '', executablePath: '', argTemplate: '"{path}"', isDefault: false };
    } catch (e) {
      ideError = String(e);
    }
  }

  async function setDefaultIde(id: number) {
    const target = ides.find((i) => i.id === id);
    if (!target) return;
    const saved = await api.upsertIde({
      id: target.id,
      name: target.name,
      executablePath: target.executablePath,
      argTemplate: target.argTemplate,
      isDefault: true,
    });
    ides = ides.map((i) => ({ ...i, isDefault: i.id === saved.id }));
  }

  async function removeIde(id: number) {
    await api.deleteIde(id);
    ides = ides.filter((i) => i.id !== id);
  }

  onMount(load);

  const labelFont = 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--mute)';
  /** Editorial chrome (Ember): section `<h2>`s read stronger than field labels. */
  const sectionTitleFont = $derived(
    chrome.sectionLabelCasing === 'sentence'
      ? 'font-family:var(--font-mono); font-size:10.5px; letter-spacing:.12em; color:var(--ink); font-weight:500;'
      : labelFont,
  );
  const inputStyle = 'font-family:var(--font-mono); font-size:12px; border-radius:var(--radius)';
</script>

<div class="flex min-h-0 flex-1 flex-col">
  <PageHeader title={chrome.sectionLabelCasing === 'sentence' ? 'Settings' : 'SETTINGS'} />

  <div class="min-h-0 flex-1 overflow-auto bg-bg">
    <div class="mx-auto flex w-full max-w-3xl flex-col gap-8 px-6 py-8">
      {#if loading}
        <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">loading…</p>
      {:else}
        <!-- Theme picker -->
        <section class="space-y-3 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
          <h2 style={sectionTitleFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Appearance' : 'APPEARANCE'}</h2>
          <div class="grid grid-cols-3 gap-3">
            {#each themeSamples as ts (ts.id)}
              {@const active = theme.current === ts.id}
              <button
                type="button"
                onclick={() => theme.set(ts.id)}
                class="flex flex-col gap-2 border p-3 text-left transition-colors"
                class:border-accent={active}
                class:border-hair={!active}
                style="border-radius:var(--radius); {active ? 'background:var(--accent-wash)' : ''}"
              >
                <div class="flex items-center justify-between">
                  <span class="text-ink" style="font-size:13px; font-weight:500">{ts.label}</span>
                  {#if active}
                    <span
                      class="px-1.5 py-0.5 text-accent"
                      style="font-family:var(--font-mono); font-size:10px; letter-spacing:.08em; background:var(--accent-soft); border-radius:var(--radius-sm)"
                    >ACTIVE</span>
                  {/if}
                </div>
                <div class="text-mute" style="font-size:11.5px">{ts.blurb}</div>
                <!-- mini swatch row -->
                <div
                  class="flex h-4 overflow-hidden border border-hair"
                  style="border-radius:var(--radius-sm)"
                  data-theme={ts.id}
                >
                  <span class="flex-1" style="background:var(--bg)"></span>
                  <span class="flex-1" style="background:var(--paper)"></span>
                  <span class="flex-1" style="background:var(--ink)"></span>
                  <span class="flex-1" style="background:var(--accent)"></span>
                  <span class="flex-1" style="background:var(--ok)"></span>
                  <span class="flex-1" style="background:var(--danger)"></span>
                </div>
              </button>
            {/each}
          </div>
        </section>

        <div class="flex flex-col gap-6">
          <section class="space-y-4 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
            <h2 style={sectionTitleFont}>{chrome.sectionLabelCasing === 'sentence' ? 'GitLab' : 'GITLAB'}</h2>

            <label class="flex flex-col gap-1.5">
              <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Base URL' : 'BASE URL'}</span>
              <input
                type="url"
                bind:value={baseUrl}
                placeholder="https://gitlab.example.com"
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink placeholder:text-mute"
                style={inputStyle}
              />
            </label>

            <label class="flex flex-col gap-1.5">
              <div class="flex items-center justify-between">
                <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Personal access token' : 'PAT'}</span>
                {#if hasPat}
                  <span class="text-ok" style="font-family:var(--font-mono); font-size:10.5px">stored</span>
                {:else}
                  <span class="text-warn" style="font-family:var(--font-mono); font-size:10.5px">not set</span>
                {/if}
              </div>
              <input
                type="password"
                bind:value={pat}
                placeholder={hasPat ? '••••••••  (leave blank to keep)' : 'glpat-…'}
                autocomplete="off"
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink placeholder:text-mute"
                style={inputStyle}
              />
              {#if hasPat}
                <label class="flex items-center gap-2 text-mute" style="font-size:11px">
                  <input type="checkbox" bind:checked={clearPat} />
                  Delete stored PAT
                </label>
              {/if}
            </label>

            <div class="grid grid-cols-2 gap-4">
              <label class="flex flex-col gap-1.5">
                <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Sync interval (s)' : 'SYNC INTERVAL (S)'}</span>
                <input
                  type="number"
                  min="30"
                  bind:value={syncIntervalSeconds}
                  class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink"
                  style={inputStyle}
                />
              </label>
              <div class="flex flex-col gap-1.5">
                <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Signed in as' : 'SIGNED IN AS'}</span>
                <div
                  class="border border-hair bg-bg px-3 py-2 text-subink"
                  style={inputStyle}
                >
                  {currentUsername ?? '— (unknown until first sync)'}
                </div>
              </div>
            </div>
          </section>

          <section class="space-y-4 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
            <h2 style={sectionTitleFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Jenkins' : 'JENKINS'}</h2>
            <p class="text-mute" style="font-size:12px; line-height:1.5">
              Firestarter calls Jenkins with an API token (HTTP Basic). In GitLab, point project webhooks at your Jenkins
              job (or use the Generic Webhook Trigger plugin) so push/MR events reach the same pipelines you inspect here.
            </p>

            <label class="flex flex-col gap-1.5">
              <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Base URL' : 'BASE URL'}</span>
              <input
                type="url"
                bind:value={jenkinsBaseUrl}
                placeholder="https://jenkins.example.com"
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink placeholder:text-mute"
                style={inputStyle}
              />
            </label>

            <label class="flex flex-col gap-1.5">
              <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Username' : 'USERNAME'}</span>
              <input
                type="text"
                bind:value={jenkinsUsername}
                placeholder="jenkins login id"
                autocomplete="username"
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink placeholder:text-mute"
                style={inputStyle}
              />
            </label>

            <label class="flex flex-col gap-1.5">
              <div class="flex items-center justify-between">
                <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'API token' : 'API TOKEN'}</span>
                {#if jenkinsHasToken}
                  <span class="text-ok" style="font-family:var(--font-mono); font-size:10.5px">stored</span>
                {:else}
                  <span class="text-warn" style="font-family:var(--font-mono); font-size:10.5px">not set</span>
                {/if}
              </div>
              <input
                type="password"
                bind:value={jenkinsApiToken}
                placeholder={jenkinsHasToken ? '••••••••  (leave blank to keep)' : 'user API token'}
                autocomplete="current-password"
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink placeholder:text-mute"
                style={inputStyle}
              />
              {#if jenkinsHasToken}
                <label class="flex items-center gap-2 text-mute" style="font-size:11px">
                  <input type="checkbox" bind:checked={jenkinsClearToken} />
                  Delete stored API token
                </label>
              {/if}
            </label>

            <div class="flex flex-wrap items-center gap-3">
              <button
                type="button"
                onclick={testJenkinsConnection}
                disabled={jenkinsTesting}
                class="border border-hair-strong bg-paper px-3 py-1.5 text-[12px] text-ink hover:bg-panel-2 disabled:opacity-50"
                style="border-radius:var(--radius)"
              >
                {jenkinsTesting ? 'Testing…' : 'Test connection'}
              </button>
              {#if jenkinsTestNote}
                <span
                  class={jenkinsTestNote === 'Jenkins API reachable.' ? 'text-ok' : 'text-danger'}
                  style="font-size:12px"
                >
                  {jenkinsTestNote}
                </span>
              {/if}
            </div>
          </section>

          <section class="space-y-4 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
            <h2 style={sectionTitleFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Workspace' : 'WORKSPACE'}</h2>

            <label class="flex flex-col gap-1.5">
              <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Projects root' : 'PROJECTS ROOT'}</span>
              <input
                type="text"
                bind:value={projectsRoot}
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink"
                style={inputStyle}
              />
            </label>

            <label class="flex flex-col gap-1.5">
              <span style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Temp root (extension runs)' : 'TEMP ROOT'}</span>
              <input
                type="text"
                bind:value={tempRoot}
                class="focus-ring border border-hair-strong bg-bg px-3 py-2 text-ink"
                style={inputStyle}
              />
            </label>
          </section>

          {#if error}<p class="text-danger" style="font-size:12px">{error}</p>{/if}
          {#if saving}
            <p class="text-mute" style="font-family:var(--font-mono); font-size:11px">Saving…</p>
          {:else if toast}
            <p class="text-ok" style="font-size:12px">{toast}</p>
          {/if}
        </div>

        <section class="space-y-4 border border-hair bg-paper p-5" style="border-radius:var(--radius-lg)">
          <h2 style={sectionTitleFont}>{chrome.sectionLabelCasing === 'sentence' ? 'IDEs' : 'IDES'}</h2>

          {#if ides.length > 0}
            <ul class="divide-y divide-hair border border-hair bg-bg" style="border-radius:var(--radius)">
              {#each ides as ide (ide.id)}
                <li class="flex items-center gap-3 px-3 py-2">
                  <div class="min-w-0 flex-1">
                    <div class="flex items-center gap-2">
                      <span class="text-ink" style="font-size:13px; font-weight:500">{ide.name}</span>
                      {#if ide.isDefault}
                        <span
                          class="px-1.5 py-0.5 text-accent"
                          style="font-family:var(--font-mono); font-size:10px; letter-spacing:.08em; background:var(--accent-soft); border-radius:var(--radius-sm)"
                        >DEFAULT</span>
                      {/if}
                    </div>
                    <div class="truncate text-mute" style="font-family:var(--font-mono); font-size:11px">{ide.executablePath}</div>
                    <div class="truncate text-mute" style="font-family:var(--font-mono); font-size:11px">{ide.argTemplate}</div>
                  </div>
                  {#if !ide.isDefault}
                    <button
                      type="button"
                      onclick={() => setDefaultIde(ide.id)}
                      class="border border-hair-strong bg-paper px-2 py-1 text-[11px] text-ink hover:bg-panel-2"
                      style="border-radius:var(--radius-sm)"
                    >Make default</button>
                  {/if}
                  <button
                    type="button"
                    onclick={() => removeIde(ide.id)}
                    class="border border-danger px-2 py-1 text-[11px] text-danger hover:bg-danger-soft"
                    style="border-radius:var(--radius-sm)"
                  >Remove</button>
                </li>
              {/each}
            </ul>
          {:else}
            <p class="text-mute" style="font-size:12px">No IDEs registered yet.</p>
          {/if}

          <div class="flex flex-col gap-2 border border-hair bg-bg p-3" style="border-radius:var(--radius)">
            <div style={labelFont}>{chrome.sectionLabelCasing === 'sentence' ? 'Add IDE' : 'ADD IDE'}</div>
            <input
              type="text"
              bind:value={ideDraft.name}
              placeholder="Name (e.g. VS Code)"
              class="focus-ring border border-hair-strong bg-paper px-2 py-1.5 text-ink"
              style={inputStyle}
            />
            <input
              type="text"
              bind:value={ideDraft.executablePath}
              placeholder="C:\Users\you\AppData\Local\Programs\Microsoft VS Code\Code.exe"
              class="focus-ring border border-hair-strong bg-paper px-2 py-1.5 text-ink"
              style={inputStyle}
            />
            <input
              type="text"
              bind:value={ideDraft.argTemplate}
              placeholder={'"{path}"'}
              class="focus-ring border border-hair-strong bg-paper px-2 py-1.5 text-ink"
              style={inputStyle}
            />
            <p class="text-mute" style="font-size:11px">
              Tokens: <span style="font-family:var(--font-mono)">{'{path}'}</span> repo folder,
              <span style="font-family:var(--font-mono)">{'{solution}'}</span> .sln/.slnx at repo root (falls back to folder).
            </p>
            <label class="flex items-center gap-2 text-mute" style="font-size:11px">
              <input type="checkbox" bind:checked={ideDraft.isDefault} />
              Set as default
            </label>
            {#if ideError}<p class="text-danger" style="font-size:11.5px">{ideError}</p>{/if}
            <button
              type="button"
              onclick={addIde}
              disabled={!ideDraft.name || !ideDraft.executablePath}
              class="self-start border border-hair-strong bg-paper px-3 py-1.5 text-[12px] text-ink disabled:opacity-50 hover:bg-panel-2"
              style="border-radius:var(--radius)"
            >Add IDE</button>
          </div>
        </section>
      {/if}
    </div>
  </div>
</div>
