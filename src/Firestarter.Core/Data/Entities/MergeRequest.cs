namespace Firestarter.Core.Data.Entities;

public class MergeRequest
{
    public int Id { get; set; }
    public long GitlabIid { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string? AuthorUsername { get; set; }
    public string? AssigneeUsernames { get; set; }
    public string? ReviewerUsernames { get; set; }
    public string WebUrl { get; set; } = string.Empty;
    public bool Draft { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Open discussion thread count, refreshed from GitLab when the user visits the MR's Discussion tab. Null until first visit.</summary>
    public int? OpenDiscussions { get; set; }

    /// <summary>True when the authenticated GitLab user has approved this MR. Refreshed on MR-detail load and when the user clicks Approve.</summary>
    public bool ApprovedByCurrentUser { get; set; }

    public Project? Project { get; set; }
}
