using Firestarter.Core.Stats;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class StatsSummaryHandler(ApiCallStatsService stats) : IIpcHandler
{
    readonly ApiCallStatsService _stats = stats;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var summary = await _stats.GetSummaryAsync(ct);
        return summary;
    }
}

public class StatsListHandler(ApiCallStatsService stats) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ApiCallStatsService _stats = stats;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<ListRequest>(Options) ?? new ListRequest();
        var entries = await _stats.ListAsync(dto.Take ?? 200, dto.From, dto.To, ct);
        return new { entries };
    }

    class ListRequest
    {
        public int? Take { get; set; }
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
    }
}
