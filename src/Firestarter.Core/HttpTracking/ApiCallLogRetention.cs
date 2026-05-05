using Firestarter.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Firestarter.Core.HttpTracking;

/// <summary>
/// Periodically deletes <see cref="Firestarter.Core.Data.Entities.ApiCallLog"/> rows older than 30 days
/// so the local SQLite file stays bounded even with chatty integrations.
/// </summary>
public sealed partial class ApiCallLogRetention(IServiceScopeFactory scopeFactory, ILogger<ApiCallLogRetention> logger) : BackgroundService
{
    static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();
                var cutoff = DateTimeOffset.UtcNow - RetentionPeriod;
                var deleted = await db.ApiCallLogs
                    .Where(x => x.Timestamp < cutoff)
                    .ExecuteDeleteAsync(stoppingToken)
                    .ConfigureAwait(false);
                if (deleted > 0)
                    LogPruned(logger, deleted, cutoff);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                LogSweepFailed(logger, ex);
            }

            try { await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pruned {Deleted} ApiCallLog rows older than {Cutoff:O}")]
    static partial void LogPruned(ILogger logger, int deleted, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ApiCallLog retention sweep failed")]
    static partial void LogSweepFailed(ILogger logger, Exception exception);
}
