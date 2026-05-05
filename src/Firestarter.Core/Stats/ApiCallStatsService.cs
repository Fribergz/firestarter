using Firestarter.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Stats;

public record ApiCallEntryDto(
    long Id,
    DateTimeOffset Timestamp,
    string Method,
    string Host,
    string Path,
    int StatusCode,
    int DurationMs,
    long RequestBytes,
    long ResponseBytes,
    string Source,
    string? ErrorMessage);

public record ApiCallSummaryDto(
    int TotalLast7d,
    int TotalToday,
    int FailuresLast7d,
    double AverageDurationMsLast7d,
    IReadOnlyList<ApiCallDayDto> ByDay,
    IReadOnlyList<string> Sources);

public record ApiCallDayDto(
    /// <summary>Date in YYYY-MM-DD (UTC).</summary>
    string Date,
    /// <summary>Counts keyed by source classification (gitlab/jenkins/other).</summary>
    IReadOnlyDictionary<string, int> CountsBySource,
    int Total);

public class ApiCallStatsService(FirestarterDbContext db)
{
    readonly FirestarterDbContext _db = db;

    public async Task<ApiCallSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero);
        var weekStart = todayStart.AddDays(-6);

        var rows = await _db.ApiCallLogs
            .AsNoTracking()
            .Where(x => x.Timestamp >= weekStart)
            .Select(x => new { x.Timestamp, x.Source, x.StatusCode, x.DurationMs })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var totalToday = rows.Count(r => r.Timestamp >= todayStart);
        var failures = rows.Count(r => r.StatusCode == 0 || r.StatusCode >= 400);
        var avg = rows.Count == 0 ? 0d : rows.Average(r => (double)r.DurationMs);

        var sources = rows
            .Select(r => string.IsNullOrEmpty(r.Source) ? "other" : r.Source)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var byDay = new List<ApiCallDayDto>(7);
        for (var i = 6; i >= 0; i--)
        {
            var dayStart = todayStart.AddDays(-i);
            var dayEnd = dayStart.AddDays(1);
            var dayRows = rows.Where(r => r.Timestamp >= dayStart && r.Timestamp < dayEnd).ToList();
            var counts = dayRows
                .GroupBy(r => string.IsNullOrEmpty(r.Source) ? "other" : r.Source)
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (var s in sources)
                counts.TryAdd(s, 0);
            byDay.Add(new ApiCallDayDto(
                dayStart.UtcDateTime.ToString("yyyy-MM-dd"),
                counts,
                dayRows.Count));
        }

        return new ApiCallSummaryDto(
            TotalLast7d: rows.Count,
            TotalToday: totalToday,
            FailuresLast7d: failures,
            AverageDurationMsLast7d: Math.Round(avg, 1),
            ByDay: byDay,
            Sources: sources);
    }

    public async Task<IReadOnlyList<ApiCallEntryDto>> ListAsync(
        int take,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 10_000);
        IQueryable<Data.Entities.ApiCallLog> q = _db.ApiCallLogs.AsNoTracking();
        if (from.HasValue) q = q.Where(x => x.Timestamp >= from.Value);
        if (to.HasValue) q = q.Where(x => x.Timestamp < to.Value);
        var rows = await q
            .OrderByDescending(x => x.Timestamp)
            .Take(take)
            .Select(x => new ApiCallEntryDto(
                x.Id,
                x.Timestamp,
                x.Method,
                x.Host,
                x.Path,
                x.StatusCode,
                x.DurationMs,
                x.RequestBytes,
                x.ResponseBytes,
                x.Source,
                x.ErrorMessage))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }
}
