namespace Firestarter.Core.Data.Entities;

public class GitlabSettings
{
    public int Id { get; set; }
    public string? BaseUrl { get; set; }
    public string? PatCredentialName { get; set; }
    public int SyncIntervalSeconds { get; set; } = 300;
    public string? CurrentUsername { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
