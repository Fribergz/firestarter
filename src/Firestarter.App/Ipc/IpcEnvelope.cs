using System.Text.Json;

namespace Firestarter.App.Ipc;

public record IpcEnvelope(string Id, string Type, JsonElement? Payload);

public record IpcSuccess(string Id, bool Ok, object? Result)
{
    public static IpcSuccess Of(string id, object? result) => new(id, true, result);
}

public record IpcError(string Id, bool Ok, string Error)
{
    public static IpcError Of(string id, string error) => new(id, false, error);
}
