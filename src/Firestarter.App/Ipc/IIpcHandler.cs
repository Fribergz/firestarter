using System.Text.Json;

namespace Firestarter.App.Ipc;

public interface IIpcHandler
{
    Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct);
}
