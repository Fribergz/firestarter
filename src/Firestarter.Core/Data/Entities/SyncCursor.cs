namespace Firestarter.Core.Data.Entities;

public class SyncCursor
{
    public int Id { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? LastGitlabUpdatedAt { get; set; }
    public string? Etag { get; set; }
}
