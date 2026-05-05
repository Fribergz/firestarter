using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.NET;
using System.Text.Json;

namespace Firestarter.App.Ipc;

public partial class IpcDispatcher(IServiceScopeFactory scopeFactory, ILogger<IpcDispatcher> logger)
{
    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    PhotinoWindow? _window;

    public void BindWindow(PhotinoWindow window) => _window = window;

    public void Dispatch(string rawMessage) => _ = DispatchAsync(rawMessage);

    async Task DispatchAsync(string raw)
    {
        IpcEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IpcEnvelope>(raw, JsonOpts);
        }
        catch (Exception ex)
        {
            LogMalformed(logger, ex);
            return;
        }
        if (envelope is null)
        {
            LogNullEnvelope(logger);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetKeyedService<IIpcHandler>(envelope.Type);
            if (handler is null)
            {
                SendError(envelope.Id, $"No handler for '{envelope.Type}'");
                return;
            }
            var result = await handler.HandleAsync(envelope.Payload, CancellationToken.None);
            SendSuccess(envelope.Id, result);
        }
        catch (Exception ex)
        {
            LogHandlerThrew(logger, envelope.Type, ex);
            SendError(envelope.Id, ex.Message);
        }
    }

    void SendSuccess(string id, object? result) => Send(IpcSuccess.Of(id, result));
    void SendError(string id, string error) => Send(IpcError.Of(id, error));

    void Send(object reply)
    {
        if (_window is null)
        {
            LogReplyDropped(logger);
            return;
        }
        var json = JsonSerializer.Serialize(reply, JsonOpts);
        _window.Invoke(() => _window.SendWebMessage(json));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed IPC message")]
    static partial void LogMalformed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Null IPC envelope")]
    static partial void LogNullEnvelope(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "IPC handler '{Type}' threw")]
    static partial void LogHandlerThrew(ILogger logger, string type, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "IPC reply dropped — window not bound")]
    static partial void LogReplyDropped(ILogger logger);
}
