using Firestarter.Core.Data;
using Firestarter.Core.GitLab;
using Firestarter.Core.MergeRequests;
using Firestarter.Core.Projects;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class ProjectsListHandler(ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<ListPayload>(Options) ?? new ListPayload();
        var items = await _read.ListAsync(dto.Filter, dto.Take ?? 200, ct);
        return new { projects = items };
    }

    class ListPayload
    {
        public string? Filter { get; set; }
        public int? Take { get; set; }
    }
}

public class ProjectGetHandler(ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectReadService _read = read;

    /// <summary>Local database snapshot only. GitLab refresh runs via <c>sync.start</c> (background worker).</summary>
    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<GetPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var detail = await _read.GetAsync(dto.ProjectId.Value, ct);
        return detail is null ? null : new { project = detail };
    }

    class GetPayload { public int? ProjectId { get; set; } }
}

public class ProjectPipelinesCachedListHandler(ProjectPipelineService pipelines) : IIpcHandler
{
    readonly ProjectPipelineService _pipelines = pipelines;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        var items = await _pipelines.ListCachedSnapshotsAsync(ct);
        return new { items };
    }
}

public class ProjectPipelinesListHandler(ProjectPipelineService pipelines) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectPipelineService _pipelines = pipelines;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<ListPipelinesPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var (items, jenkinsConfigured, error, cachedAt) =
            await _pipelines.ListRecentAsync(dto.ProjectId.Value, dto.Take ?? 10, ct);
        return new { pipelines = items, jenkinsConfigured, error, cachedAt };
    }

    class ListPipelinesPayload
    {
        public int? ProjectId { get; set; }
        public int? Take { get; set; }
    }
}

public class MergeRequestGetHandler(ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<GetPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        var mr = await _read.GetMergeRequestAsync(dto.ProjectId.Value, dto.Iid.Value, ct);
        return mr is null ? null : new { mr };
    }

    class GetPayload
    {
        public int? ProjectId { get; set; }
        public long? Iid { get; set; }
    }
}

public class MergeRequestDiscussionCreateHandler(FirestarterDbContext db, IGitLabClientFactory factory) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly FirestarterDbContext _db = db;
    readonly IGitLabClientFactory _factory = factory;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<Payload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        if (string.IsNullOrWhiteSpace(dto.Body)) throw new ArgumentException("body required");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == dto.ProjectId.Value, ct)
            ?? throw new InvalidOperationException($"Project {dto.ProjectId} not found");

        var client = await _factory.CreateAsync(ct)
            ?? throw new InvalidOperationException("GitLab not configured (missing base URL or PAT)");

        var discussions = client.GetMergeRequest(project.GitlabId).Discussions(dto.Iid.Value);
        var created = discussions.Add(new NGitLab.Models.MergeRequestDiscussionCreate { Body = dto.Body! });

        var mr = await _db.MergeRequests
            .FirstOrDefaultAsync(m => m.ProjectId == dto.ProjectId.Value && m.GitlabIid == dto.Iid.Value, ct);
        if (mr is not null)
        {
            mr.OpenDiscussions = (mr.OpenDiscussions ?? 0) + 1;
            await _db.SaveChangesAsync(ct);
        }

        // Discussions don't carry their own URL — return the first note's id so the UI can deep-link into GitLab.
        var firstNoteId = created?.Notes?.FirstOrDefault()?.Id;
        return new
        {
            id = created?.Id,
            webUrl = firstNoteId is null ? null : $"#note_{firstNoteId}",
        };
    }

    class Payload
    {
        public int? ProjectId { get; set; }
        public long? Iid { get; set; }
        public string? Body { get; set; }
    }
}

/// <summary>Common payload used by all per-tab MR endpoints (overview / commits / changes / discussions).</summary>
file class TabPayload
{
    public int? ProjectId { get; set; }
    public long? Iid { get; set; }
}

public class MergeRequestOverviewGetHandler(MergeRequestTabService tabs) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly MergeRequestTabService _tabs = tabs;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<TabPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        var ov = await _tabs.GetOverviewAsync(dto.ProjectId.Value, dto.Iid.Value, ct);
        return ov is null ? null : new { overview = ov };
    }
}

public class MergeRequestCommitsGetHandler(MergeRequestTabService tabs) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly MergeRequestTabService _tabs = tabs;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<TabPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        var commits = await _tabs.GetCommitsAsync(dto.ProjectId.Value, dto.Iid.Value, ct);
        return new { commits };
    }
}

public class MergeRequestChangesGetHandler(MergeRequestTabService tabs) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly MergeRequestTabService _tabs = tabs;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<TabPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        var changes = await _tabs.GetChangesAsync(dto.ProjectId.Value, dto.Iid.Value, ct);
        return new { changes };
    }
}

public class MergeRequestDiscussionsGetHandler(MergeRequestTabService tabs) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly MergeRequestTabService _tabs = tabs;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<TabPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        var discussions = await _tabs.GetDiscussionsAsync(dto.ProjectId.Value, dto.Iid.Value, ct);
        return new { discussions };
    }
}

public class CountersHandler(ProjectReadService read) : IIpcHandler
{
    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var c = await _read.GetCountersAsync(ct);
        return new
        {
            authoredOrAssignedOpen = c.AuthoredOrAssignedOpen,
            reviewerOpen = c.ReviewerOpen,
            projects = c.Projects,
            branches = c.Branches,
        };
    }
}

public class ProjectMarkVisitedHandler(ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<Payload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var ok = await _read.MarkProjectVisitedAsync(dto.ProjectId.Value, ct);
        return new { ok };
    }

    class Payload { public int? ProjectId { get; set; } }
}

public class ProjectSetStarredHandler(ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<Payload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var ok = await _read.SetProjectStarredAsync(dto.ProjectId.Value, dto.Starred ?? false, ct);
        return new { ok };
    }

    class Payload
    {
        public int? ProjectId { get; set; }
        public bool? Starred { get; set; }
    }
}

public class MergeRequestsListMineHandler(ProjectReadService read) : IIpcHandler
{
    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var items = await _read.ListMyMergeRequestsAsync(ct);
        return new { items };
    }
}

public class MergeRequestsListReviewerHandler(ProjectReadService read) : IIpcHandler
{
    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        // DB-only. Open-discussion counts are cached on the MR row via the Discussion-tab fetch on
        // MR detail; the current-user approval flag is set by the Overview-tab fetch and the Approve
        // action — so this list is always serving freshly-cached data without any GitLab round-trips.
        var items = await _read.ListReviewerMergeRequestsAsync(ct);
        return new { items };
    }
}

public class MergeRequestApproveHandler(MergeRequestTabService tabs) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly MergeRequestTabService _tabs = tabs;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<TabPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null || dto.Iid is null) throw new ArgumentException("projectId and iid required");
        var approvals = await _tabs.ApproveAsync(dto.ProjectId.Value, dto.Iid.Value, ct);
        return approvals is null ? null : new { approvals };
    }
}

