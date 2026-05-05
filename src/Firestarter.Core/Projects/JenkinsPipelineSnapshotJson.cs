using Firestarter.Core.Jenkins;
using System.Text.Json;

namespace Firestarter.Core.Projects;

internal static class JenkinsPipelineSnapshotJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string Serialize(IReadOnlyList<JenkinsBuildSummaryDto> builds) =>
        JsonSerializer.Serialize(builds, Options);

    internal static IReadOnlyList<JenkinsBuildSummaryDto>? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<JenkinsBuildSummaryDto>>(json, Options);
        }
        catch
        {
            return null;
        }
    }
}
