namespace Firestarter.Core.Data.Entities;

public class JenkinsSettings
{
    public int Id { get; set; }
    public string? BaseUrl { get; set; }
    /// <summary>Jenkins user id used with the API token (HTTP Basic).</summary>
    public string? Username { get; set; }
    public string? ApiTokenCredentialName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
