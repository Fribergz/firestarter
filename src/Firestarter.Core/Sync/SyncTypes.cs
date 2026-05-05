namespace Firestarter.Core.Sync;

public enum SyncScope
{
    FullProjects = 1,
    Project = 3,
}

public record SyncRequest(SyncScope Scope, int? ProjectId = null, string Reason = "");

public enum SyncState
{
    Idle = 0,
    Running = 1,
    Error = 2,
}

public record SyncStatusSnapshot
{
    public SyncState State { get; init; }
    public string? CurrentScope { get; init; }
    public string? CurrentItem { get; init; }
    public int Processed { get; init; }
    public int? Total { get; init; }
    public DateTimeOffset? LastStartedAt { get; init; }
    public DateTimeOffset? LastFinishedAt { get; init; }
    public string? LastError { get; init; }
    public int QueueDepth { get; init; }
}
