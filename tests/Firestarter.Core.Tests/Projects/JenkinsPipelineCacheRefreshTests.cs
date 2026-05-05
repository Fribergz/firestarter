using Firestarter.Core.Jenkins;
using Firestarter.Core.Projects;

namespace Firestarter.Core.Tests.Projects;

public class JenkinsPipelineCacheRefreshTests
{
    static JenkinsBuildSummaryDto B(int n, string status) =>
        new(n, status, "http://x/", null, null, null, null, null);

    [Fact]
    public void Empty_cache_and_no_job_build_is_fresh()
    {
        Assert.True(JenkinsPipelineCacheRefresh.SnapshotStillFresh([], null));
    }

    [Fact]
    public void Empty_cache_with_job_build_is_stale()
    {
        Assert.False(JenkinsPipelineCacheRefresh.SnapshotStillFresh([], new JenkinsLastBuildDto(1, "u", false, "SUCCESS")));
    }

    [Fact]
    public void Cached_builds_but_no_last_build_is_stale()
    {
        Assert.False(JenkinsPipelineCacheRefresh.SnapshotStillFresh([B(3, "SUCCESS")], null));
    }

    [Fact]
    public void Same_number_and_success_reuses()
    {
        var last = new JenkinsLastBuildDto(10, "u", false, "SUCCESS");
        Assert.True(JenkinsPipelineCacheRefresh.SnapshotStillFresh([B(10, "SUCCESS"), B(9, "FAILURE")], last));
    }

    [Fact]
    public void Same_number_but_cache_still_running_while_job_finished_is_stale()
    {
        var last = new JenkinsLastBuildDto(10, "u", false, "SUCCESS");
        Assert.False(JenkinsPipelineCacheRefresh.SnapshotStillFresh([B(10, "RUNNING")], last));
    }

    [Fact]
    public void Same_number_job_still_building_matches_running_cache()
    {
        var last = new JenkinsLastBuildDto(10, "u", true, null);
        Assert.True(JenkinsPipelineCacheRefresh.SnapshotStillFresh([B(10, "RUNNING")], last));
    }

    [Fact]
    public void New_higher_build_number_is_stale()
    {
        var last = new JenkinsLastBuildDto(11, "u", false, "SUCCESS");
        Assert.False(JenkinsPipelineCacheRefresh.SnapshotStillFresh([B(10, "SUCCESS")], last));
    }

    [Fact]
    public void Lower_job_number_than_cache_is_stale()
    {
        var last = new JenkinsLastBuildDto(9, "u", false, "SUCCESS");
        Assert.False(JenkinsPipelineCacheRefresh.SnapshotStillFresh([B(10, "SUCCESS")], last));
    }
}
