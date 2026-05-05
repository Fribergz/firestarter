using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class PingHandler : IIpcHandler
{
    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var message = payload?.TryGetProperty("message", out var m) == true ? m.GetString() : null;
        object result = new
        {
            echo = message ?? "(no message)",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
        };
        return Task.FromResult<object?>(result);
    }
}
