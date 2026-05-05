using Firestarter.Core.Data;
using Firestarter.Core.Jenkins;
using Firestarter.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Projects;

public record ProjectListItem(
    int Id,
    long GitlabId,
    string PathWithNamespace,
    string Name,
    string? Description,
    string? DefaultBranch,
    string WebUrl,
    DateTimeOffset? LastActivityAt,
    bool Archived,
    bool Starred,
    int OpenMergeRequestCount,
    int BranchCount);

public record BranchDto(int Id, string Name, string Sha, bool IsDefault, bool IsProtected, DateTimeOffset UpdatedAt);

public record MergeRequestDto(
    int Id,
    long Iid,
    string Title,
    string State,
    string SourceBranch,
    string TargetBranch,
    string? AuthorUsername,
    string? AssigneeUsernames,
    string? ReviewerUsernames,
    string WebUrl,
    bool Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record MergeRequestListItem(
    int Id,
    long Iid,
    int ProjectId,
    string ProjectPath,
    string Title,
    string State,
    string SourceBranch,
    string TargetBranch,
    string? AuthorUsername,
    string? AssigneeUsernames,
    string? ReviewerUsernames,
    string WebUrl,
    bool Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? OpenDiscussions = null);

public record ProjectDetail(
    int Id,
    long GitlabId,
    string PathWithNamespace,
    string Name,
    string? Description,
    string? DefaultBranch,
    string WebUrl,
    string? JenkinsJobPath,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? BranchesMrsSyncedAt,
    bool Archived,
    IReadOnlyList<BranchDto> Branches,
    IReadOnlyList<MergeRequestDto> OpenMergeRequests,
    bool JenkinsPipelinesConfigured,
    IReadOnlyList<JenkinsBuildSummaryDto>? JenkinsPipelinesCache,
    DateTimeOffset? JenkinsPipelinesCachedAt);

public class ProjectReadService(FirestarterDbContext db, SettingsService settings)
{
    readonly FirestarterDbContext _db = db;
    readonly SettingsService _settings = settings;

    public async Task<IReadOnlyList<ProjectListItem>> ListAsync(string? filter, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 500);
        var projects = _db.Projects.AsNoTracking().Where(p => !p.Archived);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim().ToLower();
            projects = projects.Where(p =>
                EF.Functions.Like(p.PathWithNamespace.ToLower(), $"%{f}%") ||
                EF.Functions.Like(p.Name.ToLower(), $"%{f}%"));
        }

        var rows = await projects
            .OrderByDescending(p => p.Starred)
            .ThenByDescending(p => p.LastActivityAt)
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.GitlabId,
                p.PathWithNamespace,
                p.Name,
                p.Description,
                p.DefaultBranch,
                p.WebUrl,
                p.LastActivityAt,
                p.Archived,
                p.Starred,
                OpenMrs = _db.MergeRequests.Count(m => m.ProjectId == p.Id && m.State == "opened"),
                Branches = _db.Branches.Count(b => b.ProjectId == p.Id),
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new ProjectListItem(
            r.Id, r.GitlabId, r.PathWithNamespace, r.Name, r.Description, r.DefaultBranch, r.WebUrl,
            r.LastActivityAt, r.Archived, r.Starred, r.OpenMrs, r.Branches))];
    }

    public async Task<bool> SetProjectStarredAsync(int id, bool starred, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (project is null) return false;

        // Push to GitLab first so the local DB only reflects state that GitLab actually accepted.
        // If GitLab isn't configured, fall through to a local-only update — the user can't sync stars
        // anywhere meaningful in that case.
        var cfg = await _settings.GetOrCreateAsync(ct);
        var pat = await _settings.GetPatAsync(ct);
        if (!string.IsNullOrWhiteSpace(cfg.BaseUrl) && !string.IsNullOrEmpty(pat))
        {
            await CallGitlabStarApiAsync(cfg.BaseUrl!, pat, project.GitlabId, starred, ct).ConfigureAwait(false);
        }

        project.Starred = starred;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    static async Task CallGitlabStarApiAsync(string baseUrl, string pat, long gitlabProjectId, bool starred, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", pat);
        var endpoint = starred ? "star" : "unstar";
        var url = $"{baseUrl.TrimEnd('/')}/api/v4/projects/{gitlabProjectId}/{endpoint}";
        using var resp = await http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        // 201 (newly starred), 200 (already starred / unstarred OK), 304 (no-op): all fine.
        var status = (int)resp.StatusCode;
        if (status is 200 or 201 or 304) return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var snippet = body.Length > 240 ? body[..240] + "…" : body.Trim();
        throw new InvalidOperationException($"GitLab {endpoint} HTTP {status}: {snippet}");
    }

    public async Task<ProjectDetail?> GetAsync(int id, CancellationToken ct)
    {
        var p = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;

        var branches = await _db.Branches.AsNoTracking()
            .Where(b => b.ProjectId == id)
            .OrderByDescending(b => b.IsDefault)
            .ThenBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Name, b.Sha, b.IsDefault, b.IsProtected, b.UpdatedAt))
            .ToListAsync(ct);

        var mrs = await _db.MergeRequests.AsNoTracking()
            .Where(m => m.ProjectId == id && m.State == "opened")
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new MergeRequestDto(
                m.Id, m.GitlabIid, m.Title, m.State, m.SourceBranch, m.TargetBranch,
                m.AuthorUsername, m.AssigneeUsernames, m.ReviewerUsernames,
                m.WebUrl, m.Draft, m.CreatedAt, m.UpdatedAt))
            .ToListAsync(ct);

        var jenkins = await _settings.GetJenkinsConfigAsync(ct);
        var token = await _settings.GetJenkinsApiTokenAsync(ct);
        var jenkinsPipelinesConfigured =
            !string.IsNullOrWhiteSpace(jenkins.BaseUrl)
            && !string.IsNullOrWhiteSpace(jenkins.Username)
            && !string.IsNullOrEmpty(token)
            && !string.IsNullOrWhiteSpace(p.JenkinsJobPath);

        var pipelineCache = JenkinsPipelineSnapshotJson.Deserialize(p.JenkinsPipelinesCacheJson);

        return new ProjectDetail(
            p.Id, p.GitlabId, p.PathWithNamespace, p.Name, p.Description, p.DefaultBranch,
            p.WebUrl, p.JenkinsJobPath, p.LastActivityAt, p.BranchesMrsSyncedAt, p.Archived, branches, mrs,
            jenkinsPipelinesConfigured, pipelineCache, p.JenkinsPipelinesCachedAt);
    }

    public async Task<MergeRequestDto?> GetMergeRequestAsync(int projectId, long iid, CancellationToken ct)
    {
        return await _db.MergeRequests.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.GitlabIid == iid)
            .Select(m => new MergeRequestDto(
                m.Id, m.GitlabIid, m.Title, m.State, m.SourceBranch, m.TargetBranch,
                m.AuthorUsername, m.AssigneeUsernames, m.ReviewerUsernames,
                m.WebUrl, m.Draft, m.CreatedAt, m.UpdatedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> MarkProjectVisitedAsync(int id, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (project is null) return false;
        project.LastVisitedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CountersDto> GetCountersAsync(CancellationToken ct)
    {
        var cfg = await _settings.GetOrCreateAsync(ct);
        var me = cfg.CurrentUsername;

        int authored = 0;
        int reviewer = 0;
        if (!string.IsNullOrWhiteSpace(me))
        {
            var token = $",{me},";

            authored = await _db.MergeRequests
                .Where(m => m.State == "opened" && !m.Project!.Archived)
                .Where(m => m.AuthorUsername == me ||
                            (m.AssigneeUsernames != null &&
                                ("," + m.AssigneeUsernames + ",").Contains(token)))
                .CountAsync(ct);

            // Match ListReviewerMergeRequestsAsync: reviewers only, excluding MRs you have already approved locally.
            reviewer = await _db.MergeRequests
                .Where(m => m.State == "opened" && !m.Project!.Archived &&
                            !m.ApprovedByCurrentUser &&
                            m.ReviewerUsernames != null &&
                            ("," + m.ReviewerUsernames + ",").Contains(token))
                .CountAsync(ct);
        }

        var projectCount = await _db.Projects.CountAsync(p => !p.Archived, ct);
        var branchCount = await _db.Branches.CountAsync(b => !b.Project!.Archived, ct);
        return new CountersDto(authored, reviewer, projectCount, branchCount);
    }

    public async Task<IReadOnlyList<MergeRequestListItem>> ListMyMergeRequestsAsync(CancellationToken ct)
    {
        var cfg = await _settings.GetOrCreateAsync(ct);
        var me = cfg.CurrentUsername;
        if (string.IsNullOrWhiteSpace(me)) return [];
        var token = $",{me},";

        return await _db.MergeRequests.AsNoTracking()
            .Where(m => m.State == "opened" && !m.Project!.Archived)
            .Where(m => m.AuthorUsername == me ||
                        (m.AssigneeUsernames != null &&
                            ("," + m.AssigneeUsernames + ",").Contains(token)))
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new MergeRequestListItem(
                m.Id, m.GitlabIid, m.ProjectId,
                m.Project!.PathWithNamespace,
                m.Title, m.State, m.SourceBranch, m.TargetBranch,
                m.AuthorUsername, m.AssigneeUsernames, m.ReviewerUsernames,
                m.WebUrl, m.Draft, m.CreatedAt, m.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MergeRequestListItem>> ListReviewerMergeRequestsAsync(CancellationToken ct)
    {
        var cfg = await _settings.GetOrCreateAsync(ct);
        var me = cfg.CurrentUsername;
        if (string.IsNullOrWhiteSpace(me)) return [];
        var token = $",{me},";

        return await _db.MergeRequests.AsNoTracking()
            .Where(m => m.State == "opened" && !m.Project!.Archived &&
                        !m.ApprovedByCurrentUser &&
                        m.ReviewerUsernames != null &&
                        ("," + m.ReviewerUsernames + ",").Contains(token))
            // Ascending open thread count: unknown (null) before 0, then 1, 2, …; tie-break by recent activity.
            .OrderBy(m => m.OpenDiscussions != null)
            .ThenBy(m => m.OpenDiscussions ?? 0)
            .ThenByDescending(m => m.UpdatedAt)
            .Select(m => new MergeRequestListItem(
                m.Id, m.GitlabIid, m.ProjectId,
                m.Project!.PathWithNamespace,
                m.Title, m.State, m.SourceBranch, m.TargetBranch,
                m.AuthorUsername, m.AssigneeUsernames, m.ReviewerUsernames,
                m.WebUrl, m.Draft, m.CreatedAt, m.UpdatedAt,
                m.OpenDiscussions))
            .ToListAsync(ct);
    }
}

public record CountersDto(int AuthoredOrAssignedOpen, int ReviewerOpen, int Projects, int Branches);
