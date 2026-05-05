using System.Threading.Channels;
using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Firestarter.Core.HttpTracking;

/// <summary>
/// Buffers <see cref="ApiCallLog"/> rows from <see cref="HttpCallObserver"/> and flushes them to SQLite
/// in small batches on a background task so each outbound HTTP request does not block on the DB.
/// </summary>
public sealed partial class HttpCallRecorder(IServiceScopeFactory scopeFactory, ILogger<HttpCallRecorder> logger) : BackgroundService
{
    readonly Channel<ApiCallLog> _channel = Channel.CreateBounded<ApiCallLog>(new BoundedChannelOptions(2048)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    public void Enqueue(ApiCallLog entry) => _channel.Writer.TryWrite(entry);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Mark the channel complete so any pending WaitToReadAsync resolves cleanly even
        // if the consumer is between iterations when the stoppingToken fires.
        _channel.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<ApiCallLog>(64);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                buffer.Clear();
                while (buffer.Count < 100 && _channel.Reader.TryRead(out var item))
                    buffer.Add(item);
                if (buffer.Count == 0) continue;

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();
                    db.ApiCallLogs.AddRange(buffer);
                    await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LogPersistFailed(logger, buffer.Count, ex);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to persist {Count} API call log rows")]
    static partial void LogPersistFailed(ILogger logger, int count, Exception exception);
}
