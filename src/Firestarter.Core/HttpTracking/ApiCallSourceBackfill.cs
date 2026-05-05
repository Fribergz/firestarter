using Firestarter.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Firestarter.Core.HttpTracking;

/// <summary>
/// One-shot startup task that rewrites the stored <c>Source</c> of every <see cref="Data.Entities.ApiCallLog"/>
/// row to match the current <see cref="HttpSourceClassifier"/> rule set, so updates to the rules apply to
/// historical entries without a backfill migration.
/// </summary>
public sealed partial class ApiCallSourceBackfill(IServiceScopeFactory scopeFactory, ILogger<ApiCallSourceBackfill> logger) : BackgroundService
{
    static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();

            var hosts = await db.ApiCallLogs
                .Select(x => x.Host)
                .Distinct()
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            var totalUpdated = 0;
            foreach (var host in hosts)
            {
                if (stoppingToken.IsCancellationRequested) return;
                var expected = HttpSourceClassifier.Classify(host);
                var updated = await db.ApiCallLogs
                    .Where(x => x.Host == host && x.Source != expected)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Source, expected), stoppingToken)
                    .ConfigureAwait(false);
                totalUpdated += updated;
            }

            if (totalUpdated > 0)
                LogReclassified(logger, totalUpdated);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogBackfillFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reclassified Source on {Count} ApiCallLog rows")]
    static partial void LogReclassified(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ApiCallLog source backfill failed")]
    static partial void LogBackfillFailed(ILogger logger, Exception exception);
}
