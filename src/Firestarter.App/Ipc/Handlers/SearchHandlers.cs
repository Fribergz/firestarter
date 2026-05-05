using Firestarter.Core.Search;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class ProjectSearchHandler(ProjectSearchService search) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectSearchService _search = search;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var req = payload?.Deserialize<ProjectSearchPayload>(Options) ?? new ProjectSearchPayload();
        var hits = await _search.SearchAsync(req.Query, req.Limit ?? 25, ct);
        return new { hits };
    }

    class ProjectSearchPayload
    {
        public string? Query { get; set; }
        public int? Limit { get; set; }
    }
}
