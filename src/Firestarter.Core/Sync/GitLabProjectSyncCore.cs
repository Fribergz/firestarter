using Firestarter.Core.Data;
using Firestarter.Core.Jenkins;
using Firestarter.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NGitLab.Models;
using Branch = Firestarter.Core.Data.Entities.Branch;
using MergeRequest = Firestarter.Core.Data.Entities.MergeRequest;
using Project = Firestarter.Core.Data.Entities.Project;

namespace Firestarter.Core.Sync;

/// <summary>
/// GitLab branch + open MR sync for a single local project (shared by background sync and on-demand refresh).
/// </summary>
public static partial class GitLabProjectSyncCore
{
    public static async Task RefreshBranchesAndOpenMrsAsync(
        NGitLab.IGitLabClient client,
        FirestarterDbContext db,
        SettingsService settings,
        int localProjectId,
        ILogger? logger,
        CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == localProjectId, ct);
        if (project is null || project.Archived) return;

        await SyncBranchesAsync(client, db, project, ct);
        await SyncOpenMergeRequestsAsync(client, db, project, ct);
        await TrySyncJenkinsJobPathFromGitLabAsync(client, db, settings, project.GitlabId, logger, ct);
        project.BranchesMrsSyncedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static async Task TrySyncJenkinsJobPathFromGitLabAsync(
        NGitLab.IGitLabClient client,
        FirestarterDbContext db,
        SettingsService settings,
        long gitlabProjectId,
        ILogger? logger,
        CancellationToken ct)
    {
        var jenkins = await settings.GetOrCreateJenkinsAsync(ct);
        if (string.IsNullOrWhiteSpace(jenkins.BaseUrl)) return;

        var entity = await db.Projects.FirstOrDefaultAsync(x => x.GitlabId == gitlabProjectId, ct);
        if (entity is null) return;
        if (!string.IsNullOrWhiteSpace(entity.JenkinsJobPath)) return;
        // Negative result is also cached: once the hooks endpoint has been inspected and no Jenkins-pointing
        // hook was found, do not hammer GitLab on every sync. Cleared by the manual reset on the Sync page.
        if (entity.JenkinsJobPathProbedAt is not null) return;

        try
        {
            foreach (var hook in client.GetRepository(gitlabProjectId).ProjectHooks.All)
            {
                ct.ThrowIfCancellationRequested();
                var hookUrl = hook.Url is null ? "" : hook.Url.ToString();
                var path = JenkinsGitLabWebhookResolver.TryDeriveJobPathFromHookUrl(hookUrl, jenkins.BaseUrl!);
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (path.Length > 512) path = path[..512];
                entity.JenkinsJobPath = path;
                break;
            }

            // Tombstone the probe so subsequent syncs skip the hooks GET. Inside the try so that transient
            // failures (e.g. 403, network blip) leave the timestamp null and let the next sync retry.
            entity.JenkinsJobPathProbedAt = DateTimeOffset.UtcNow;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            if (logger is not null) LogHooksReadFailed(logger, gitlabProjectId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read GitLab hooks for project {GitlabProjectId}")]
    static partial void LogHooksReadFailed(ILogger logger, long gitlabProjectId, Exception exception);

    static async Task SyncBranchesAsync(NGitLab.IGitLabClient client, FirestarterDbContext db, Project project, CancellationToken ct)
    {
        var repo = client.GetRepository(project.GitlabId);
        var seen = new HashSet<string>();
        foreach (var b in repo.Branches.All)
        {
            ct.ThrowIfCancellationRequested();
            seen.Add(b.Name);
            var existing = await db.Branches.FirstOrDefaultAsync(x => x.ProjectId == project.Id && x.Name == b.Name, ct);
            if (existing is null)
            {
                db.Branches.Add(new Branch
                {
                    ProjectId = project.Id,
                    Name = b.Name,
                    Sha = b.Commit?.Id.ToString() ?? string.Empty,
                    IsDefault = b.Default,
                    IsProtected = b.Protected,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.Sha = b.Commit?.Id.ToString() ?? existing.Sha;
                existing.IsDefault = b.Default;
                existing.IsProtected = b.Protected;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        var stale = await db.Branches
            .Where(x => x.ProjectId == project.Id && !seen.Contains(x.Name))
            .ToListAsync(ct);
        db.Branches.RemoveRange(stale);
    }

    static async Task SyncOpenMergeRequestsAsync(NGitLab.IGitLabClient client, FirestarterDbContext db, Project project, CancellationToken ct)
    {
        var mrClient = client.GetMergeRequest(project.GitlabId);
        var seen = new HashSet<long>();
        foreach (var mr in mrClient.AllInState(MergeRequestState.opened))
        {
            ct.ThrowIfCancellationRequested();
            seen.Add(mr.Iid);
            await UpsertMergeRequestAsync(db, project.Id, mr, ct);
        }

        var nowClosed = await db.MergeRequests
            .Where(x => x.ProjectId == project.Id && x.State == "opened" && !seen.Contains(x.GitlabIid))
            .ToListAsync(ct);
        foreach (var mr in nowClosed)
        {
            mr.State = "closed";
            mr.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public static async Task UpsertMergeRequestAsync(FirestarterDbContext db, int localProjectId, NGitLab.Models.MergeRequest mr, CancellationToken ct)
    {
        var assignees = JoinUsernames(mr.Assignees) ?? mr.Assignee?.Username;
        var reviewers = JoinUsernames(mr.Reviewers);
        var existing = await db.MergeRequests.FirstOrDefaultAsync(
            x => x.ProjectId == localProjectId && x.GitlabIid == mr.Iid, ct);
        if (existing is null)
        {
            db.MergeRequests.Add(new MergeRequest
            {
                GitlabIid = mr.Iid,
                ProjectId = localProjectId,
                Title = mr.Title ?? string.Empty,
                State = mr.State?.ToString() ?? "opened",
                SourceBranch = mr.SourceBranch ?? string.Empty,
                TargetBranch = mr.TargetBranch ?? string.Empty,
                AuthorUsername = mr.Author?.Username,
                AssigneeUsernames = assignees,
                ReviewerUsernames = reviewers,
                WebUrl = mr.WebUrl ?? string.Empty,
                Draft = mr.Draft,
                CreatedAt = new DateTimeOffset(mr.CreatedAt.ToUniversalTime(), TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(mr.UpdatedAt.ToUniversalTime(), TimeSpan.Zero),
            });
        }
        else
        {
            existing.Title = mr.Title ?? existing.Title;
            existing.State = mr.State?.ToString() ?? existing.State;
            existing.SourceBranch = mr.SourceBranch ?? existing.SourceBranch;
            existing.TargetBranch = mr.TargetBranch ?? existing.TargetBranch;
            existing.AuthorUsername = mr.Author?.Username;
            existing.AssigneeUsernames = assignees;
            existing.ReviewerUsernames = reviewers;
            existing.WebUrl = mr.WebUrl ?? existing.WebUrl;
            existing.Draft = mr.Draft;
            existing.UpdatedAt = new DateTimeOffset(mr.UpdatedAt.ToUniversalTime(), TimeSpan.Zero);
        }
    }

    static string? JoinUsernames(User[]? users)
    {
        if (users is null || users.Length == 0) return null;
        var names = users
            .Where(u => !string.IsNullOrWhiteSpace(u.Username))
            .Select(u => u.Username)
            .ToArray();
        return names.Length == 0 ? null : string.Join(",", names);
    }
}
