using Firestarter.Core.Jenkins;
using System.Text.Json;

namespace Firestarter.Core.Tests.Jenkins;

public class JenkinsRestClientTests
{
    [Theory]
    [InlineData("melody-master/number-porting-in-worker_master", "melody-master/number-porting-in-worker_master")]
    [InlineData("project/melody-master/number-porting-in-worker_master", "melody-master/number-porting-in-worker_master")]
    [InlineData("/project/melody-master/number-porting-in-worker_master", "melody-master/number-porting-in-worker_master")]
    [InlineData(
        "https://jenkins.example.com/project/melody-master/number-porting-in-worker_master",
        "melody-master/number-porting-in-worker_master")]
    public void NormalizeJobPath_maps_webhook_style_to_stored_segments(string input, string expected)
    {
        Assert.Equal(expected, JenkinsRestClient.NormalizeJobPath(input));
    }

    [Fact]
    public void BuildJobPrefixUrl_uses_job_segments()
    {
        var url = JenkinsRestClient.BuildJobPrefixUrl(
            "https://jenkins.example.com/",
            "melody-master/number-porting-in-worker_master");
        Assert.Equal(
            "https://jenkins.example.com/job/melody-master/job/number-porting-in-worker_master",
            url);
    }

    [Fact]
    public void TryExtractVersionTagFromBuildApi_reads_TAG_NAME_parameter()
    {
        using var doc = JsonDocument.Parse("""
            {
              "actions": [
                {
                  "parameters": [
                    { "name": "BRANCH", "value": "main" },
                    { "name": "TAG_NAME", "value": "v2.4.1" }
                  ]
                }
              ]
            }
            """);
        Assert.Equal("v2.4.1", JenkinsRestClient.TryExtractVersionTagFromBuildApi(doc.RootElement));
    }

    [Fact]
    public void TryParseWfapiSteps_collects_flow_nodes_and_first_failure()
    {
        using var doc = JsonDocument.Parse("""
            {
              "stages": [
                {
                  "name": "Build",
                  "status": "FAILURE",
                  "stageFlowNodes": [
                    { "name": "Compile", "status": "SUCCESS" },
                    { "name": "Test", "status": "FAILURE" }
                  ]
                }
              ]
            }
            """);
        var ok = JenkinsRestClient.TryParseWfapiSteps(doc.RootElement, out var steps, out var failedOn);
        Assert.True(ok);
        Assert.Equal(2, steps!.Count);
        Assert.Equal("SUCCESS", steps[0].Status);
        Assert.Equal("FAILURE", steps[1].Status);
        Assert.Contains("Test", failedOn!, StringComparison.Ordinal);
    }
}
