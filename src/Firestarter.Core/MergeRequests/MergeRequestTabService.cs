using Firestarter.Core.Data;
using Firestarter.Core.GitLab;
using Microsoft.EntityFrameworkCore;
using NGitLab.Models;

namespace Firestarter.Core.MergeRequests;

/// <summary>
/// Read-only DTOs returned by the per-tab MR detail endpoints. Live-fetched on demand from GitLab —
/// none of this is persisted in the local DB.
/// </summary>
public record MergeRequestApprovalsDto(
    bool Approved,
    int ApprovalsRequired,
    int ApprovalsLeft,
    bool UserHasApproved,
    bool UserCanApprove,
    string[] ApprovedBy,
    string[] SuggestedApprovers);

public record MergeRequestOverviewDto(
    string? Description,
    int UserNotesCount,
    string ChangesCount,
    int? DivergedCommitsCount,
    string[] Labels,
    string? BaseSha,
    string? HeadSha,
    string? StartSha,
    MergeRequestApprovalsDto? Approvals);

public record MergeRequestCommitDto(
    string Id,
    string ShortId,
    string Title,
    string? Message,
    string? AuthorName,
    string? AuthorEmail,
    DateTime AuthoredDate,
    string? WebUrl);

public record MergeRequestFileChangeDto(
    string OldPath,
    string NewPath,
    bool NewFile,
    bool DeletedFile,
    bool RenamedFile,
    string Diff);

public record MergeRequestNoteDto(
    long Id,
    string? Author,
    string? Body,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool System,
    bool Resolved,
    bool Resolvable);

public record MergeRequestDiscussionDto(
    string Id,
    bool IndividualNote,
    MergeRequestNoteDto[] Notes);

public class MergeRequestTabService(FirestarterDbContext db, IGitLabClientFactory factory)
{
    readonly FirestarterDbContext _db = db;
    readonly IGitLabClientFactory _factory = factory;

    public async Task<MergeRequestOverviewDto?> GetOverviewAsync(int projectId, long iid, CancellationToken ct)
    {
        var (client, gitlabProjectId) = await ResolveAsync(projectId, ct);
        if (client is null) return null;

        var mrClient = client.GetMergeRequest(gitlabProjectId);
        var mr = await mrClient.GetByIidAsync(iid, new SingleMergeRequestQuery
        {
            IncludeDivergedCommitsCount = true,
            IncludeRebaseInProgress = false,
        }, ct);
        if (mr is null) return null;

        MergeRequestApprovalsDto? approvalsDto = null;
        bool? userHasApproved = null;
        try
        {
            var approvals = mrClient.ApprovalClient(iid).Approvals;
            if (approvals is not null)
            {
                userHasApproved = approvals.UserHasApproved;
                approvalsDto = new MergeRequestApprovalsDto(
                    approvals.Approved,
                    approvals.ApprovalsRequired,
                    approvals.ApprovalsLeft,
                    approvals.UserHasApproved,
                    approvals.UserCanApprove,
                    [.. (approvals.ApprovedBy ?? []).Select(a => a.User?.Username ?? string.Empty).Where(u => u.Length > 0)],
                    [.. (approvals.SuggestedApprovers ?? []).Select(u => u?.Username ?? string.Empty).Where(u => u.Length > 0)]);
            }
        }
        catch
        {
            // Approval API may be unavailable on free tier or on certain project setups. Non-fatal.
        }

        if (userHasApproved.HasValue)
        {
            await UpdateMrCacheAsync(projectId, iid, approvedByCurrentUser: userHasApproved.Value, ct: ct);
        }

        return new MergeRequestOverviewDto(
            mr.Description,
            mr.UserNotesCount,
            mr.ChangesCount ?? string.Empty,
            mr.DivergedCommitsCount,
            mr.Labels ?? [],
            mr.DiffRefs?.BaseSha,
            mr.DiffRefs?.HeadSha,
            mr.DiffRefs?.StartSha,
            approvalsDto);
    }

    public async Task<IReadOnlyList<MergeRequestCommitDto>> GetCommitsAsync(int projectId, long iid, CancellationToken ct)
    {
        var (client, gitlabProjectId) = await ResolveAsync(projectId, ct);
        if (client is null) return [];

        var commits = client.GetMergeRequest(gitlabProjectId).Commits(iid).All;
        var list = new List<MergeRequestCommitDto>();
        foreach (var c in commits)
        {
            ct.ThrowIfCancellationRequested();
            list.Add(new MergeRequestCommitDto(
                c.Id.ToString() ?? string.Empty,
                c.ShortId ?? string.Empty,
                c.Title ?? string.Empty,
                c.Message,
                c.AuthorName,
                c.AuthorEmail,
                c.AuthoredDate,
                c.WebUrl));
        }
        return list;
    }

    public async Task<IReadOnlyList<MergeRequestFileChangeDto>> GetChangesAsync(int projectId, long iid, CancellationToken ct)
    {
        var (client, gitlabProjectId) = await ResolveAsync(projectId, ct);
        if (client is null) return [];

        var change = client.GetMergeRequest(gitlabProjectId).Changes(iid).MergeRequestChange;
        if (change?.Changes is null) return [];

        return [.. change.Changes.Select(c => new MergeRequestFileChangeDto(
            c.OldPath ?? string.Empty,
            c.NewPath ?? string.Empty,
            c.NewFile,
            c.DeletedFile,
            c.RenamedFile,
            c.Diff ?? string.Empty))];
    }

    public async Task<IReadOnlyList<MergeRequestDiscussionDto>> GetDiscussionsAsync(int projectId, long iid, CancellationToken ct)
    {
        var (client, gitlabProjectId) = await ResolveAsync(projectId, ct);
        if (client is null) return [];

        var discussions = client.GetMergeRequest(gitlabProjectId).Discussions(iid).All;
        var list = new List<MergeRequestDiscussionDto>();
        foreach (var d in discussions)
        {
            ct.ThrowIfCancellationRequested();
            var notes = (d.Notes ?? []).Select(n => new MergeRequestNoteDto(
                n.Id,
                n.Author?.Username,
                n.Body,
                n.CreatedAt,
                n.UpdatedAt,
                n.System,
                n.Resolved,
                n.Resolvable)).ToArray();
            list.Add(new MergeRequestDiscussionDto(d.Id ?? string.Empty, d.IndividualNote, notes));
        }
        // Cache the open-thread count so the reviewing list can render it without a live GitLab call.
        var (open, _) = CountOpenDiscussionStats(list);
        await UpdateMrCacheAsync(projectId, iid, openDiscussions: open, ct: ct);
        return list;
    }

    /// <summary>
    /// Counts threads GitLab would treat as open (unresolved resolvable, or any non-resolvable user note)
    /// and, among those, how many have at least two user-authored notes.
    /// </summary>
    public static (int Open, int OpenWithReply) CountOpenDiscussionStats(IReadOnlyList<MergeRequestDiscussionDto> discussions)
    {
        int open = 0;
        int withReply = 0;
        foreach (var d in discussions)
        {
            var notes = d.Notes;
            if (notes.Length == 0) continue;
            if (!IsDiscussionOpen(notes)) continue;
            open++;
            if (HasUserReply(notes)) withReply++;
        }
        return (open, withReply);
    }

    static bool IsDiscussionOpen(MergeRequestNoteDto[] notes)
    {
        var resolvable = notes.Where(n => n.Resolvable).ToArray();
        if (resolvable.Length > 0) return resolvable.Any(n => !n.Resolved);
        return notes.Any(n => !n.System);
    }

    static bool HasUserReply(MergeRequestNoteDto[] notes) => notes.Count(n => !n.System) >= 2;

    /// <summary>
    /// Persists the cached open-discussion count and/or current-user approval flag onto the local MR row.
    /// Called from MR-detail tab loads so the reviewing list can render without live GitLab calls.
    /// </summary>
    async Task UpdateMrCacheAsync(int projectId, long iid, int? openDiscussions = null, bool? approvedByCurrentUser = null, CancellationToken ct = default)
    {
        if (openDiscussions is null && approvedByCurrentUser is null) return;
        var row = await _db.MergeRequests.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.GitlabIid == iid, ct);
        if (row is null) return;
        var dirty = false;
        if (openDiscussions is int oc && row.OpenDiscussions != oc)
        {
            row.OpenDiscussions = oc;
            dirty = true;
        }
        if (approvedByCurrentUser is bool ap && row.ApprovedByCurrentUser != ap)
        {
            row.ApprovedByCurrentUser = ap;
            dirty = true;
        }
        if (dirty) await _db.SaveChangesAsync(ct);
    }

    /// <summary>Approves the MR on behalf of the current user. Returns the resulting approvals snapshot, or null if GitLab is not configured.</summary>
    public async Task<MergeRequestApprovalsDto?> ApproveAsync(int projectId, long iid, CancellationToken ct)
    {
        var (client, gitlabProjectId) = await ResolveAsync(projectId, ct);
        if (client is null) return null;

        var approvalClient = client.GetMergeRequest(gitlabProjectId).ApprovalClient(iid);
        var approvals = approvalClient.ApproveMergeRequest(new MergeRequestApproveRequest());
        if (approvals is null) return null;

        // Persist so the reviewing list filters this MR out on next visit without re-asking GitLab.
        await UpdateMrCacheAsync(projectId, iid, approvedByCurrentUser: approvals.UserHasApproved, ct: ct);

        return new MergeRequestApprovalsDto(
            approvals.Approved,
            approvals.ApprovalsRequired,
            approvals.ApprovalsLeft,
            approvals.UserHasApproved,
            approvals.UserCanApprove,
            [.. (approvals.ApprovedBy ?? []).Select(a => a.User?.Username ?? string.Empty).Where(u => u.Length > 0)],
            [.. (approvals.SuggestedApprovers ?? []).Select(u => u?.Username ?? string.Empty).Where(u => u.Length > 0)]);
    }

    async Task<(NGitLab.IGitLabClient? Client, long GitlabProjectId)> ResolveAsync(int projectId, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found");
        var client = await _factory.CreateAsync(ct);
        return (client, project.GitlabId);
    }
}
