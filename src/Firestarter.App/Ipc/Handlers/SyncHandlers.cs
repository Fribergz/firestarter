using Firestarter.Core.Data;
using Firestarter.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class SyncStartHandler(GitLabSyncService sync) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly GitLabSyncService _sync = sync;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var req = payload?.Deserialize<SyncStartPayload>(Options) ?? new SyncStartPayload();
        var scope = ParseScope(req.Scope);
        var enqueued = _sync.Enqueue(new SyncRequest(scope, req.ProjectId, req.Reason ?? "ui"));
        return Task.FromResult<object?>(new { enqueued, scope = scope.ToString() });
    }

    static SyncScope ParseScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return SyncScope.FullProjects;
        return Enum.TryParse<SyncScope>(scope, ignoreCase: true, out var parsed)
            ? parsed
            : SyncScope.FullProjects;
    }

    class SyncStartPayload
    {
        public string? Scope { get; set; }
        public int? ProjectId { get; set; }
        public string? Reason { get; set; }
    }
}

public class SyncStatusHandler(SyncStatusHub hub) : IIpcHandler
{
    readonly SyncStatusHub _hub = hub;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var s = _hub.Snapshot;
        return Task.FromResult<object?>(new
        {
            state = s.State.ToString(),
            currentScope = s.CurrentScope,
            currentItem = s.CurrentItem,
            processed = s.Processed,
            total = s.Total,
            lastStartedAt = s.LastStartedAt,
            lastFinishedAt = s.LastFinishedAt,
            lastError = s.LastError,
            queueDepth = s.QueueDepth,
        });
    }
}

/// <summary>
/// Clears <c>JenkinsJobPath</c> and <c>JenkinsJobPathProbedAt</c> on every project so the next full
/// sync re-derives the Jenkins job path from project hooks. Triggered from the Sync page when the
/// user wants to pick up newly-added Jenkins webhooks.
/// </summary>
public partial class SyncResetJenkinsJobPathsHandler(FirestarterDbContext db, ILogger<SyncResetJenkinsJobPathsHandler> logger) : IIpcHandler
{
    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var reset = await db.Projects
            .Where(p => p.JenkinsJobPath != null || p.JenkinsJobPathProbedAt != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.JenkinsJobPath, (string?)null)
                .SetProperty(p => p.JenkinsJobPathProbedAt, (DateTimeOffset?)null), ct);
        LogReset(logger, reset);
        return new { reset };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reset Jenkins job path on {Reset} projects")]
    static partial void LogReset(ILogger logger, int reset);
}
