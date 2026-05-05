namespace Firestarter.Core.Data.Entities;

public enum ExtensionRunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    TimedOut = 4,
    Cancelled = 5,
}

public class ExtensionRun
{
    public int Id { get; set; }
    public int ExtensionId { get; set; }
    public int ProjectId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string? CommitSha { get; set; }
    public ExtensionRunStatus Status { get; set; } = ExtensionRunStatus.Pending;
    public int? ExitCode { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? StdoutPath { get; set; }
    public string? StderrPath { get; set; }
    public string? StatsJson { get; set; }
    public string? ErrorMessage { get; set; }

    public Extension? Extension { get; set; }
    public Project? Project { get; set; }
}
