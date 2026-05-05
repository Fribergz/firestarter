using Firestarter.Core.Jenkins;

namespace Firestarter.Core.Projects;

/// <summary>Decides whether the persisted pipeline list can be reused without calling the heavy builds+enrichment APIs.</summary>
internal static class JenkinsPipelineCacheRefresh
{
    /// <returns>
    /// True when the job’s <see cref="JenkinsLastBuildDto"/> matches the newest row in <paramref name="cachedNewestFirst"/>
    /// (same build number and same effective status). Also true when both the cache and Jenkins report no builds.
    /// </returns>
    internal static bool SnapshotStillFresh(
        IReadOnlyList<JenkinsBuildSummaryDto> cachedNewestFirst,
        JenkinsLastBuildDto? jobLastBuild)
    {
        if (cachedNewestFirst.Count == 0)
            return jobLastBuild is null;

        if (jobLastBuild is null)
            return false;

        var top = cachedNewestFirst[0];
        if (jobLastBuild.Number != top.Number)
            return false;

        var live = EffectiveStatusFromLast(jobLastBuild);
        return string.Equals(
            NormalizeStatus(top.Status),
            NormalizeStatus(live),
            StringComparison.OrdinalIgnoreCase);
    }

    static string EffectiveStatusFromLast(JenkinsLastBuildDto b) =>
        b.Building ? "RUNNING" : (b.Result ?? "PENDING");

    static string NormalizeStatus(string s) =>
        string.IsNullOrWhiteSpace(s) ? "PENDING" : s.Trim();
}
