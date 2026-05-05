namespace Firestarter.Core.Data.Entities;

public class Project
{
    public int Id { get; set; }
    public long GitlabId { get; set; }
    public string PathWithNamespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DefaultBranch { get; set; }
    public string WebUrl { get; set; } = string.Empty;
    public string? SshUrlToRepo { get; set; }
    public string? HttpUrlToRepo { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? LastVisitedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>When branches and open MRs were last pulled from GitLab for this project.</summary>
    public DateTimeOffset? BranchesMrsSyncedAt { get; set; }
    public bool Archived { get; set; }
    /// <summary>True when the user has marked this project as a favourite — pinned to the top of the list and surfaced in the starred filter.</summary>
    public bool Starred { get; set; }
    /// <summary>Slash-separated Jenkins job path (e.g. <c>folder/my-pipeline</c>) for REST calls.</summary>
    public string? JenkinsJobPath { get; set; }

    /// <summary>
    /// Stamped when GitLab project hooks have been read and inspected for a Jenkins URL — even when no
    /// matching hook was found. Lets the sync skip <c>/api/v4/projects/{id}/hooks</c> on subsequent runs
    /// for projects with no Jenkins integration. Cleared by the manual reset on the Sync page.
    /// </summary>
    public DateTimeOffset? JenkinsJobPathProbedAt { get; set; }

    /// <summary>Serialized Jenkins build summaries (JSON) from the last successful pipeline list refresh.</summary>
    public string? JenkinsPipelinesCacheJson { get; set; }

    public DateTimeOffset? JenkinsPipelinesCachedAt { get; set; }

    /// <summary>The <c>take</c> (1–50) passed to the last successful Jenkins recent-builds list that filled <see cref="JenkinsPipelinesCacheJson"/>.</summary>
    public int? JenkinsPipelinesCacheRunTake { get; set; }

    /// <summary>True when that list returned fewer than <see cref="JenkinsPipelinesCacheRunTake"/> rows (no more builds for that depth).</summary>
    public bool JenkinsPipelinesCacheComplete { get; set; }

    public List<Branch> Branches { get; set; } = [];
    public List<MergeRequest> MergeRequests { get; set; } = [];
}
