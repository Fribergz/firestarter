using Firestarter.Core.Extensions;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class ExtensionsRootGetHandler(ExtensionRegistry registry) : IIpcHandler
{
    readonly ExtensionRegistry _registry = registry;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var root = await _registry.GetRootAsync(ct);
        return new { root };
    }
}

public class ExtensionsRootSetHandler(ExtensionRegistry registry) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ExtensionRegistry _registry = registry;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<RootPayload>(Options) ?? new RootPayload();
        await _registry.SetRootAsync(dto.Root, ct);
        return new { root = await _registry.GetRootAsync(ct) };
    }

    class RootPayload { public string? Root { get; set; } }
}

public class ExtensionsScanHandler(ExtensionRegistry registry) : IIpcHandler
{
    readonly ExtensionRegistry _registry = registry;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var result = await _registry.ScanAsync(ct);
        return new { added = result.Added, updated = result.Updated, removed = result.Removed, errors = result.Errors };
    }
}

public class ExtensionsListHandler(ExtensionRegistry registry) : IIpcHandler
{
    readonly ExtensionRegistry _registry = registry;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var list = await _registry.ListAsync(ct);
        return new { extensions = list };
    }
}

public class ExtensionsSetEnabledHandler(ExtensionRegistry registry) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ExtensionRegistry _registry = registry;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<Payload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.Id is null) throw new ArgumentException("id required");
        if (dto.Enabled is null) throw new ArgumentException("enabled required");
        var view = await _registry.SetEnabledAsync(dto.Id.Value, dto.Enabled.Value, ct);
        return new { extension = view };
    }

    class Payload
    {
        public int? Id { get; set; }
        public bool? Enabled { get; set; }
    }
}

public class ExtensionsSetSettingsHandler(ExtensionRegistry registry) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ExtensionRegistry _registry = registry;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<Payload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.Id is null) throw new ArgumentException("id required");
        var view = await _registry.SetSettingsAsync(dto.Id.Value, dto.Values, ct);
        return new { extension = view };
    }

    class Payload
    {
        public int? Id { get; set; }
        public Dictionary<string, string?>? Values { get; set; }
    }
}

public class ExtensionsRunHandler(ExtensionRunner runner) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ExtensionRunner _runner = runner;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<RunPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ExtensionId is null) throw new ArgumentException("extensionId required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");

        var run = await _runner.RunAsync(new ExtensionRunRequest(
            dto.ExtensionId.Value, dto.ProjectId.Value, dto.Branch, dto.Parameters), ct);

        return new
        {
            id = run.Id,
            status = run.Status.ToString(),
            exitCode = run.ExitCode,
            startedAt = run.StartedAt,
            finishedAt = run.FinishedAt,
            statsJson = run.StatsJson,
            errorMessage = run.ErrorMessage,
            stdoutPath = run.StdoutPath,
            stderrPath = run.StderrPath,
        };
    }

    class RunPayload
    {
        public int? ExtensionId { get; set; }
        public int? ProjectId { get; set; }
        public string? Branch { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
    }
}

public class ExtensionRunLogHandler(ExtensionRunHistory history) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ExtensionRunHistory _history = history;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<LogPayload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.RunId is null) throw new ArgumentException("runId required");
        return await _history.GetLogAsync(dto.RunId.Value, ct);
    }

    class LogPayload { public int? RunId { get; set; } }
}

public class ExtensionRunsListHandler(ExtensionRunHistory history) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ExtensionRunHistory _history = history;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<ListPayload>(Options) ?? new ListPayload();
        var runs = await _history.ListAsync(dto.ExtensionId, dto.ProjectId, dto.Take ?? 50, ct);
        return new { runs };
    }

    class ListPayload
    {
        public int? ExtensionId { get; set; }
        public int? ProjectId { get; set; }
        public int? Take { get; set; }
    }
}
