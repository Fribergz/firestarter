# Firestarter

A lightweight Windows desktop companion for day-to-day work against GitLab and
Jenkins. Firestarter keeps a local cache of the projects, branches and merge
requests you care about so navigation stays fast even on busy servers, and it
plugs straight into your existing IDE, terminal and file explorer.

## What it does

- **Projects** — searchable, filterable, sortable list of every project you
  have access to. Filter by namespace, branch count, open MRs, last activity,
  or starred status. Click a star to toggle favourites — stars sync two-way
  with GitLab so they follow you across machines.
- **Merge requests** — separate "authored" and "reviewing" inboxes with the
  same filter/sort UX as the projects list. Open the MR detail page for
  commits, file changes, discussions and approvals.
- **Jenkins pipelines** — pipeline list and status per project, with one-click
  build trigger. Jenkins job paths are auto-discovered from project webhooks.
- **Workspace launchers** — clone-or-update the project, then open it in your
  IDE, file explorer or terminal. Configurable IDEs (VS Code, Rider, etc.).
- **Extensions** — run reusable scripts (PowerShell, Bash, .NET) against any
  project + branch combo and inspect their stdout/stderr.
- **Sync** — background poller pulls projects, branches, open MRs and starred
  status from GitLab on a configurable interval. Manual "Full project sync"
  and "Reset Jenkins hook cache" buttons on the Sync page.
- **Statistics** — every outbound HTTP request the app makes (GitLab, Jenkins,
  git, other) is recorded. The Statistics page surfaces KPI counts, a 7-day
  stacked-by-source chart, and a Chrome-DevTools-style network table with
  per-column filters and pagination. 30-day retention is enforced
  automatically.
- **Self-update** — every hour the app polls a JSON manifest in a public
  GitLab project. When a newer build is published, the status bar swaps the
  "ready" indicator for a clickable update pill; one click downloads the zip,
  hands off to a PowerShell installer, and relaunches the new build.

## Stack

| Layer | Tech |
|---|---|
| Desktop host | [Photino.NET](https://github.com/tryphotino/photino.NET) running .NET 10 on Windows |
| Backend | C# 13 / .NET 10, EF Core 10 + SQLite, hosted services, `LoggerMessage` source-gen |
| GitLab API | [NGitLab](https://github.com/ubisoft/NGitLab) v11 + targeted raw HTTP calls |
| Frontend | [Svelte 5](https://svelte.dev/) (runes), TypeScript, [Tailwind CSS 4](https://tailwindcss.com/), Vite |
| IPC | Custom request/response over `PhotinoWindow.SendWebMessage` with keyed handler registry |
| Storage | Local SQLite at `%LOCALAPPDATA%\Firestarter\firestarter.db` |
| Credentials | Windows Credential Manager (PAT, Jenkins token) |

## Repository layout

```
src/
  Firestarter.App/              Photino host, IPC dispatcher, IPC handlers
    Ipc/
      Handlers/                 One file per feature group (Projects, Sync, Stats, Update, ...)
    Program.cs                  Entry point — wires DI, applies migrations, opens the window
  Firestarter.Core/             All business logic, EF entities, hosted services
    Data/Entities/              Project, Branch, MergeRequest, ApiCallLog, ...
    Migrations/                 EF Core SQLite migrations
    Sync/                       GitLabSyncService (background poller) + project sync core
    HttpTracking/               DiagnosticListener-based recorder for outbound HTTP calls
    Stats/                      ApiCallStatsService — KPI / time-series / list queries
    Updates/                    UpdateCheckService + UpdateInstaller (self-update)
    Projects/, MergeRequests/, Jenkins/, Extensions/, Workspaces/, Ide/, Git/, Settings/, Security/
  Firestarter.Web/              Svelte 5 + Vite frontend (served from disk in dev, embedded in release)
    src/routes/                 One file per top-level page (Projects, Sync, Stats, Settings, ...)
    src/lib/components/         Shared UI (TitleBar, NavRail, AppDialog, TableHeaderFilter, ...)
    src/lib/stores/             Reactive runes-based stores (route, theme, updateStatus, ...)
    src/lib/api.ts              Typed wrappers around every IPC handler
tests/
  Firestarter.Core.Tests/       xUnit tests (Jenkins URL parsers, sync helpers, ...)
```

## Building and running

### Prerequisites

- Windows 10/11
- .NET 10 SDK (`dotnet --version` ≥ 10)
- Node.js ≥ 20 with `npm` (or `pnpm`)
- A GitLab Personal Access Token (PAT) with `read_api` scope at minimum;
  `api` if you want star-toggling to push back to GitLab. Stored in Windows
  Credential Manager via the Settings page — never written to the source tree.

### Development loop

Two terminals:

```powershell
# Terminal 1 — Vite dev server (HMR for the frontend)
cd src/Firestarter.Web
npm install
npm run dev          # serves on http://127.0.0.1:5173

# Terminal 2 — desktop host
cd src/Firestarter.App
dotnet run           # auto-detects the running Vite dev server
```

The host will use the dev server when it's reachable on `127.0.0.1:5173` and
fall back to the embedded `wwwroot` build otherwise.

### Type-checking and tests

```powershell
# Frontend type check (svelte-check + tsc)
cd src/Firestarter.Web
npm run check

# Backend build + tests
cd ../..
dotnet build Firestarter.slnx
dotnet test tests/Firestarter.Core.Tests
```

### Release build

```powershell
# Builds the Svelte assets and embeds them in Firestarter.App's wwwroot.
dotnet publish src/Firestarter.App -c Release -r win-x64 --self-contained true
```

The `BuildWeb` MSBuild target in `Firestarter.App.csproj` invokes
`npm ci && npm run build` automatically when `Configuration=Release`.

### Windows installer

[Inno Setup](https://jrsoftware.org/isdl.php) is used to package the
self-contained publish output into a single `.exe` installer that handles
shortcuts and Add/Remove Programs.

```powershell
# Publish + compile + drop Firestarter-<version>-setup.exe into dist\
pwsh installer\build-installer.ps1
```

The script auto-resolves the version from the published assembly. Override
with `-Version 1.4.2` or skip the publish step with `-SkipPublish`. Default
install location is `%LOCALAPPDATA%\Programs\Firestarter` (per-user, no UAC);
the wizard offers a system-wide elevation if requested.

## Configuration

All runtime configuration lives in the SQLite database under
`%LOCALAPPDATA%\Firestarter\firestarter.db`. There are no environment
variables or appsettings files to manage. The Settings page is the single
source of truth for:

- GitLab base URL + PAT + sync interval
- Jenkins base URL + username + API token
- Workspace clone root + temp root
- Registered IDEs (executable + arg template)
- UI theme

The two compile-time hard-codings live in:

- `src/Firestarter.Core/Updates/UpdateConstants.cs` —
  `ManifestUrl` for the self-update feed.
- `src/Firestarter.App/Firestarter.App.csproj` — `<Version>` element
  drives the version embedded in the assembly metadata, which the update
  checker compares against the manifest.

## Database migrations

Schema changes use EF Core migrations:

```powershell
# From the repo root.
dotnet ef migrations add MyChange `
  --project src/Firestarter.Core `
  --startup-project src/Firestarter.App
```

Migrations are applied automatically at startup
(`db.Database.Migrate()` in `Program.cs`). To inspect the local schema use
`sqlite3 %LOCALAPPDATA%\Firestarter\firestarter.db .schema`.

## Logging

The app uses the standard `Microsoft.Extensions.Logging` pipeline with
compile-time source-generated `[LoggerMessage]` methods throughout — no
boxing, no template parsing at runtime. There is no log file by default; logs
go to the console attached to the host process.

## License

Proprietary / internal. See `LICENSE` if present.
