using Firestarter.Core.Jenkins;

namespace Firestarter.Core.Projects;

/// <summary>Combines head-build freshness with cache depth metadata so we do not skip a full Jenkins list when the DB snapshot is partial.</summary>
internal static class JenkinsPipelineCacheReusePolicy
{
    internal static bool CanShortCircuit(
        IReadOnlyList<JenkinsBuildSummaryDto> cachedFull,
        int take,
        int? runTake,
        bool cacheComplete,
        JenkinsLastBuildDto? last)
    {
        if (cachedFull.Count == 0 || last is null)
            return false;

        var haveEnoughRows = cachedFull.Count >= take || cacheComplete;
        var takeCovered = (runTake is int rt && take <= rt) || cacheComplete;
        return haveEnoughRows
            && takeCovered
            && JenkinsPipelineCacheRefresh.SnapshotStillFresh(cachedFull, last);
    }
}
