using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Firestarter.Core.GitLab;
using Firestarter.Core.Security;
using Firestarter.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGitLab.Models;
using System.Threading.Channels;
using Project = Firestarter.Core.Data.Entities.Project;

namespace Firestarter.Core.Sync;

public partial class GitLabSyncService(IServiceScopeFactory scopeFactory, SyncStatusHub status, ICredentialStore credentials, ILogger<GitLabSyncService> logger) : BackgroundService
{
    readonly Channel<SyncRequest> _channel = Channel.CreateUnbounded<SyncRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    readonly SyncStatusHub _status = status;
    readonly ICredentialStore _credentials = credentials;

    // The 15-second scheduler tick used to hit GitlabSettings + SyncCursors every time even when nothing had changed.
    // We cache both: GitlabSettings is invalidated explicitly when the user saves new settings; the SyncCursors
    // timestamp is refreshed in-process whenever the worker writes it, so the DB read only happens on first tick.
    GitlabSettings? _cachedGitlabSettings;
    DateTimeOffset? _cachedFullProjectsLastSyncedAt;
    bool _fullProjectsCursorLoaded;

    /// <summary>Drops the cached GitLab settings snapshot so the next scheduler tick re-reads from the DB. Called from <c>settings.update</c> after a save.</summary>
    public void InvalidateSettingsCache()
    {
        _cachedGitlabSettings = null;
    }

    public bool Enqueue(SyncRequest request)
    {
        var written = _channel.Writer.TryWrite(request);
        if (written) _status.Update(s => s with { QueueDepth = s.QueueDepth + 1 });
        return written;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scheduler = Task.Run(() => ScheduleLoopAsync(stoppingToken), stoppingToken);
        var worker = Task.Run(() => WorkerLoopAsync(stoppingToken), stoppingToken);
        try
        {
            await Task.WhenAll(scheduler, worker);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    async Task WorkerLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                _status.Update(s => s with { QueueDepth = Math.Max(0, s.QueueDepth - 1) });
                try
                {
                    await RunAsync(request, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogSyncFailed(logger, request.Scope, ex);
                    _status.Update(s => s with
                    {
                        State = SyncState.Error,
                        LastError = ex.Message,
                        LastFinishedAt = DateTimeOffset.UtcNow,
                    });
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    async Task ScheduleLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            do
            {
                try
                {
                    await EvaluateScheduleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogScheduleEvalFailed(logger, ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    async Task EvaluateScheduleAsync(CancellationToken ct)
    {
        // Bail before opening a scope when the in-memory state already says nothing to do. The status
        // hub is in-process so this check is free.
        var busy = _status.Snapshot.State == SyncState.Running || _status.Snapshot.QueueDepth > 0;
        if (busy) return;

        var cachedCfg = _cachedGitlabSettings;
        if (cachedCfg is null)
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            cachedCfg = await settings.GetOrCreateAsync(ct);
            _cachedGitlabSettings = cachedCfg;
        }

        if (string.IsNullOrWhiteSpace(cachedCfg.BaseUrl)) return;

        // Credential lookup is fast (Windows Credential Manager) and avoids the DB entirely.
        if (!_credentials.Exists(cachedCfg.PatCredentialName ?? "Firestarter:GitLab:PAT")) return;

        var now = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(cachedCfg.SyncIntervalSeconds);

        if (!_fullProjectsCursorLoaded)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();
            var entity = SyncScope.FullProjects.ToString();
            var cursor = await db.SyncCursors.AsNoTracking().FirstOrDefaultAsync(x => x.Entity == entity && x.Scope == null, ct);
            _cachedFullProjectsLastSyncedAt = cursor?.LastSyncedAt;
            _fullProjectsCursorLoaded = true;
        }

        var last = _cachedFullProjectsLastSyncedAt;
        if (last is not null && now - last.Value < interval) return;

        if (Enqueue(new SyncRequest(SyncScope.FullProjects, null, "scheduled")))
            LogScheduledEnqueued(logger, SyncScope.FullProjects);
    }


    async Task RunAsync(SyncRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IGitLabClientFactory>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();

        var client = await factory.CreateAsync(ct);
        if (client is null)
        {
            _status.Update(s => s with
            {
                State = SyncState.Error,
                LastError = "GitLab not configured (missing base URL or PAT)",
                LastFinishedAt = DateTimeOffset.UtcNow,
            });
            return;
        }

        _status.Update(s => new SyncStatusSnapshot
        {
            State = SyncState.Running,
            CurrentScope = request.Scope.ToString(),
            CurrentItem = null,
            Processed = 0,
            Total = null,
            LastStartedAt = DateTimeOffset.UtcNow,
            LastFinishedAt = s.LastFinishedAt,
            LastError = null,
            QueueDepth = s.QueueDepth,
        });

        switch (request.Scope)
        {
            case SyncScope.FullProjects:
                await SyncProjectsAsync(client, db, settings, ct);
                var me = await RefreshCurrentUserAsync(client, settings, ct);
                if (me is not null) await SyncMyMergeRequestsAsync(client, db, me.Value, ct);
                break;
            case SyncScope.Project when request.ProjectId.HasValue:
                await SyncSingleProjectAsync(client, db, settings, request.ProjectId.Value, ct);
                break;
        }

        var syncedAt = await UpsertCursorAsync(db, request.Scope.ToString(), ct);
        if (request.Scope == SyncScope.FullProjects)
        {
            _cachedFullProjectsLastSyncedAt = syncedAt;
            _fullProjectsCursorLoaded = true;
        }

        _status.Update(s => s with
        {
            State = SyncState.Idle,
            CurrentScope = null,
            CurrentItem = null,
            LastFinishedAt = DateTimeOffset.UtcNow,
            LastError = null,
        });
    }

    async Task SyncProjectsAsync(NGitLab.IGitLabClient client, FirestarterDbContext db, SettingsService settings, CancellationToken ct)
    {
        var query = new ProjectQuery
        {
            Scope = ProjectQueryScope.Accessible,
            OrderBy = "last_activity_at",
            Archived = false,
        };

        int processed = 0;
        var seen = new HashSet<long>();
        await foreach (var p in client.Projects.GetAsync(query).WithCancellation(ct))
        {
            if (p.Archived) continue;
            seen.Add(p.Id);
            await UpsertProjectAsync(db, p, ct);
            await GitLabProjectSyncCore.TrySyncJenkinsJobPathFromGitLabAsync(client, db, settings, p.Id, logger, ct);
            processed++;
            if (processed % 25 == 0) await db.SaveChangesAsync(ct);
            _status.Update(s => s with { Processed = processed, CurrentItem = p.PathWithNamespace });
        }
        await db.SaveChangesAsync(ct);

        var missing = await db.Projects
            .Where(p => !p.Archived && !seen.Contains(p.GitlabId))
            .ToListAsync(ct);
        foreach (var p in missing)
        {
            p.Archived = true;
            p.UpdatedAt = DateTimeOffset.UtcNow;
        }
        if (missing.Count > 0) await db.SaveChangesAsync(ct);

        await ReconcileStarredAsync(db, settings, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls the user's starred set from GitLab (authoritative) and aligns the local <c>Starred</c> flag.
    /// Two-way sync: combined with <see cref="Projects.ProjectReadService.SetProjectStarredAsync"/>
    /// which pushes local toggles to GitLab, GitLab is always the source of truth here.
    /// NGitLab v11's <c>ProjectQuery</c> has no <c>Starred</c> filter so this hits the REST endpoint directly.
    /// </summary>
    async Task ReconcileStarredAsync(FirestarterDbContext db, SettingsService settings, CancellationToken ct)
    {
        var cfg = await settings.GetOrCreateAsync(ct).ConfigureAwait(false);
        var pat = await settings.GetPatAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrEmpty(pat)) return;

        HashSet<long> starredIds;
        try
        {
            starredIds = await FetchStarredIdsAsync(cfg.BaseUrl!, pat, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Don't fail the entire sync just because the starred sweep blew up — the project list
            // and branch/MR data are already committed at this point.
            LogStarredFetchFailed(logger, ex);
            return;
        }

        var locals = await db.Projects.ToListAsync(ct).ConfigureAwait(false);
        var changed = 0;
        foreach (var lp in locals)
        {
            var should = starredIds.Contains(lp.GitlabId);
            if (lp.Starred == should) continue;
            lp.Starred = should;
            lp.UpdatedAt = DateTimeOffset.UtcNow;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    static async Task<HashSet<long>> FetchStarredIdsAsync(string baseUrl, string pat, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", pat);
        var ids = new HashSet<long>();
        var rootUrl = baseUrl.TrimEnd('/');
        var page = 1;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{rootUrl}/api/v4/projects?starred=true&simple=true&per_page=100&page={page}";
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) break;
            var count = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                count++;
                if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                    ids.Add(idEl.GetInt64());
            }
            if (count < 100) break; // last page
            page++;
        }
        return ids;
    }

    static async Task<(long Id, string Username)?> RefreshCurrentUserAsync(NGitLab.IGitLabClient client, SettingsService settings, CancellationToken ct)
    {
        try
        {
            var session = await client.Users.GetCurrentUserAsync(ct);
            if (session is null || string.IsNullOrWhiteSpace(session.Username)) return null;
            await settings.SetCurrentUsernameAsync(session.Username, ct);
            return (session.Id, session.Username);
        }
        catch
        {
            return null;
        }
    }

    async Task SyncMyMergeRequestsAsync(NGitLab.IGitLabClient client, FirestarterDbContext db, (long Id, string Username) me, CancellationToken ct)
    {
        _status.Update(s => s with { CurrentItem = "my merge requests", Processed = 0, Total = null });

        var queries = new[]
        {
            new MergeRequestQuery { State = MergeRequestState.opened, Scope = "all", AuthorId = me.Id },
            new MergeRequestQuery { State = MergeRequestState.opened, Scope = "all", AssigneeId = me.Id },
            new MergeRequestQuery { State = MergeRequestState.opened, Scope = "all", ReviewerId = me.Id },
        };

        var byProject = new Dictionary<long, Dictionary<long, NGitLab.Models.MergeRequest>>();
        foreach (var q in queries)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var mr in client.MergeRequests.Get(q))
            {
                if (!byProject.TryGetValue(mr.ProjectId, out var inner))
                {
                    inner = [];
                    byProject[mr.ProjectId] = inner;
                }
                inner[mr.Iid] = mr;
            }
        }

        // Every MR the API still returns as open, keyed by (local project id, GitLab IID).
        var stillOpenFromApi = new HashSet<(int ProjectId, long Iid)>();
        if (byProject.Count > 0)
        {
            var gitlabIds = byProject.Keys.ToArray();
            var projects = await db.Projects
                .Where(p => gitlabIds.Contains(p.GitlabId))
                .ToDictionaryAsync(p => p.GitlabId, ct);

            int processed = 0;
            foreach (var (gitlabProjectId, mrs) in byProject)
            {
                if (!projects.TryGetValue(gitlabProjectId, out var project)) continue;
                foreach (var mr in mrs.Values)
                {
                    ct.ThrowIfCancellationRequested();
                    await GitLabProjectSyncCore.UpsertMergeRequestAsync(db, project.Id, mr, ct);
                    stillOpenFromApi.Add((project.Id, mr.Iid));
                }
                processed++;
                _status.Update(s => s with { Processed = processed, Total = byProject.Count, CurrentItem = project.PathWithNamespace });
                if (processed % 10 == 0) await db.SaveChangesAsync(ct);
            }
        }

        // Open MRs that no longer appear in the API are merged/closed on GitLab; the upsert pass never
        // sees them, so old rows can retain State "opened" forever without this clear-down.
        await MarkNotReturnedOpenMyMergeRequestsClosedAsync(db, me.Username, stillOpenFromApi, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Aligns with <see cref="SyncMyMergeRequestsAsync"/>: local rows where the current user is author,
    /// assignee, or reviewer must not stay <c>opened</c> if GitLab no longer returns them in the open set.
    /// </summary>
    static async Task MarkNotReturnedOpenMyMergeRequestsClosedAsync(
        FirestarterDbContext db,
        string? myUsername,
        HashSet<(int ProjectId, long Iid)> stillOpenFromApi,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(myUsername)) return;
        var t = $",{myUsername},";

        var candidates = await db.MergeRequests
            .Where(m => m.State == "opened" && m.Project != null && !m.Project.Archived)
            .Where(m =>
                m.AuthorUsername == myUsername
                || (m.AssigneeUsernames != null && ("," + m.AssigneeUsernames + ",").Contains(t))
                || (m.ReviewerUsernames != null && ("," + m.ReviewerUsernames + ",").Contains(t)))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var m in candidates)
        {
            if (stillOpenFromApi.Contains((m.ProjectId, m.GitlabIid)))
                continue;
            m.State = "closed";
            m.UpdatedAt = now;
        }
    }

    async Task SyncSingleProjectAsync(NGitLab.IGitLabClient client, FirestarterDbContext db, SettingsService settings, int projectId, CancellationToken ct)
    {
        await GitLabProjectSyncCore.RefreshBranchesAndOpenMrsAsync(client, db, settings, projectId, logger, ct);
    }

    static async Task UpsertProjectAsync(FirestarterDbContext db, NGitLab.Models.Project p, CancellationToken ct)
    {
        var existing = await db.Projects.FirstOrDefaultAsync(x => x.GitlabId == p.Id, ct);
        if (existing is null)
        {
            db.Projects.Add(new Project
            {
                GitlabId = p.Id,
                PathWithNamespace = p.PathWithNamespace ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                DefaultBranch = p.DefaultBranch,
                WebUrl = p.WebUrl ?? string.Empty,
                SshUrlToRepo = p.SshUrl,
                HttpUrlToRepo = p.HttpUrl,
                LastActivityAt = p.LastActivityAt == default ? null : new DateTimeOffset(p.LastActivityAt.ToUniversalTime(), TimeSpan.Zero),
                UpdatedAt = DateTimeOffset.UtcNow,
                Archived = p.Archived,
            });
        }
        else
        {
            existing.PathWithNamespace = p.PathWithNamespace ?? existing.PathWithNamespace;
            existing.Name = p.Name ?? existing.Name;
            existing.Description = p.Description;
            existing.DefaultBranch = p.DefaultBranch;
            existing.WebUrl = p.WebUrl ?? existing.WebUrl;
            existing.SshUrlToRepo = p.SshUrl;
            existing.HttpUrlToRepo = p.HttpUrl;
            if (p.LastActivityAt != default)
                existing.LastActivityAt = new DateTimeOffset(p.LastActivityAt.ToUniversalTime(), TimeSpan.Zero);
            existing.Archived = p.Archived;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    static async Task<DateTimeOffset> UpsertCursorAsync(FirestarterDbContext db, string entity, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cursor = await db.SyncCursors.FirstOrDefaultAsync(x => x.Entity == entity && x.Scope == null, ct);
        if (cursor is null)
            db.SyncCursors.Add(new SyncCursor { Entity = entity, LastSyncedAt = now });
        else
            cursor.LastSyncedAt = now;
        await db.SaveChangesAsync(ct);
        return now;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sync request {Scope} failed")]
    static partial void LogSyncFailed(ILogger logger, SyncScope scope, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduled sync evaluation failed")]
    static partial void LogScheduleEvalFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled sync enqueued: {Scope}")]
    static partial void LogScheduledEnqueued(ILogger logger, SyncScope scope);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch starred projects from GitLab")]
    static partial void LogStarredFetchFailed(ILogger logger, Exception exception);
}
