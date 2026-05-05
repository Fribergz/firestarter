namespace Firestarter.Core.Data.Entities;

public class ApiCallLog
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public long RequestBytes { get; set; }
    public long ResponseBytes { get; set; }
    /// <summary>Coarse origin classification (e.g. "gitlab", "jenkins", "other"), derived from host.</summary>
    public string Source { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
