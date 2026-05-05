using Firestarter.Core.Dotnet;
using System.Text.Json;
namespace Firestarter.App.Ipc.Handlers;

public class DotnetInteractiveRunStartHandler(InteractiveDotnetRunService run) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly InteractiveDotnetRunService _run = run;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<Payload>(Options) ?? throw new ArgumentException("payload required");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");

        var res = await _run.StartAsync(dto.ProjectId.Value, dto.Branch, dto.TargetPath, ct).ConfigureAwait(false);
        return new
        {
            ok = res.Ok,
            error = res.Error,
            pid = res.Pid,
        };
    }

    class Payload
    {
        public int? ProjectId { get; set; }
        public string? Branch { get; set; }
        public string? TargetPath { get; set; }
    }
}

public class DotnetInteractiveRunStopHandler(InteractiveDotnetRunService run) : IIpcHandler
{
    readonly InteractiveDotnetRunService _run = run;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var ok = _run.TryStop();
        return Task.FromResult<object?>(new { ok, error = ok ? null : "Failed to stop the process." });
    }
}

public class DotnetInteractiveRunStatusHandler(InteractiveDotnetRunService run) : IIpcHandler
{
    readonly InteractiveDotnetRunService _run = run;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct) =>
        Task.FromResult<object?>(new
        {
            running = _run.IsRunning,
            pid = _run.ProcessId,
        });
}
