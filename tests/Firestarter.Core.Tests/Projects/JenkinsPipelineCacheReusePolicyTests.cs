using Firestarter.Core.Jenkins;
using Firestarter.Core.Projects;

namespace Firestarter.Core.Tests.Projects;

public class JenkinsPipelineCacheReusePolicyTests
{
    static JenkinsBuildSummaryDto B(int n, string status) =>
        new(n, status, "http://x/", null, null, null, null, null);

    static JenkinsLastBuildDto Last(int n, bool building = false, string? result = "SUCCESS") =>
        new(n, "http://x/", building, result);

    [Fact]
    public void Small_cache_incomplete_run_take_blocks_short_circuit_for_larger_take()
    {
        var cached = new[] { B(5, "SUCCESS"), B(4, "SUCCESS") };
        Assert.False(JenkinsPipelineCacheReusePolicy.CanShortCircuit(
            cached,
            take: 50,
            runTake: 5,
            cacheComplete: false,
            Last(5)));
    }

    [Fact]
    public void Full_width_cache_same_run_take_allows_short_circuit_when_fresh()
    {
        var cached = Enumerable.Range(0, 50).Select(i => B(100 - i, "SUCCESS")).ToArray();
        Assert.True(JenkinsPipelineCacheReusePolicy.CanShortCircuit(
            cached,
            take: 50,
            runTake: 50,
            cacheComplete: false,
            Last(100)));
    }

    [Fact]
    public void Exhausted_jenkins_history_allows_short_circuit_even_when_take_larger_than_count()
    {
        var cached = new[] { B(3, "SUCCESS"), B(2, "SUCCESS") };
        Assert.True(JenkinsPipelineCacheReusePolicy.CanShortCircuit(
            cached,
            take: 50,
            runTake: 50,
            cacheComplete: true,
            Last(3)));
    }

    [Fact]
    public void Stale_head_blocks_short_circuit()
    {
        var cached = new[] { B(10, "RUNNING") };
        Assert.False(JenkinsPipelineCacheReusePolicy.CanShortCircuit(
            cached,
            take: 5,
            runTake: 5,
            cacheComplete: true,
            Last(10, building: false, result: "SUCCESS")));
    }
}
