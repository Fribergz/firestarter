using Firestarter.Core.Jenkins;

namespace Firestarter.Core.Projects;

/// <summary>Pipeline list snapshot read from the DB for one project (no live Jenkins calls).</summary>
public record ProjectCachedPipelinesRow(
    int ProjectId,
    string PathWithNamespace,
    DateTimeOffset? CachedAt,
    IReadOnlyList<JenkinsBuildSummaryDto> Pipelines);
