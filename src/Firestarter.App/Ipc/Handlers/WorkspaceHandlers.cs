using Firestarter.Core.Settings;
using Firestarter.Core.Workspaces;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class WorkspaceGetHandler(WorkspaceSettings workspace) : IIpcHandler
{
    readonly WorkspaceSettings _workspace = workspace;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var cfg = await _workspace.GetAsync(ct);
        return new { projectsRoot = cfg.ProjectsRoot, tempRoot = cfg.TempRoot };
    }
}

public class WorkspaceUpdateHandler(WorkspaceSettings workspace) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly WorkspaceSettings _workspace = workspace;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<WorkspaceDto>(Options) ?? new WorkspaceDto();
        var cfg = await _workspace.UpdateAsync(dto.ProjectsRoot, dto.TempRoot, ct);
        return new { projectsRoot = cfg.ProjectsRoot, tempRoot = cfg.TempRoot };
    }

    class WorkspaceDto
    {
        public string? ProjectsRoot { get; set; }
        public string? TempRoot { get; set; }
    }
}

public class ProjectOpenHandler(WorkspaceService workspace) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly WorkspaceService _workspace = workspace;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<OpenPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var result = await _workspace.OpenInIdeAsync(dto.ProjectId.Value, dto.Branch, dto.IdeId, ct);
        return new
        {
            repositoryPath = result.RepositoryPath,
            branch = result.Branch,
            commitSha = result.CommitSha,
            ideName = result.IdeName,
            ideProcessId = result.IdeProcessId,
        };
    }

    class OpenPayload
    {
        public int? ProjectId { get; set; }
        public string? Branch { get; set; }
        public int? IdeId { get; set; }
    }
}

public class ProjectOpenExplorerHandler(WorkspaceService workspace) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly WorkspaceService _workspace = workspace;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<OpenPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var path = await _workspace.OpenFolderInExplorerAsync(dto.ProjectId.Value, dto.Branch, ct);
        return new { ok = true, repositoryPath = path };
    }

    class OpenPayload
    {
        public int? ProjectId { get; set; }
        public string? Branch { get; set; }
    }
}

public class ProjectOpenTerminalHandler(WorkspaceService workspace) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly WorkspaceService _workspace = workspace;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<OpenPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var path = await _workspace.OpenFolderInTerminalAsync(dto.ProjectId.Value, dto.Branch, ct);
        return new { ok = true, repositoryPath = path };
    }

    class OpenPayload
    {
        public int? ProjectId { get; set; }
        public string? Branch { get; set; }
    }
}
