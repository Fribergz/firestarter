using Firestarter.Core.Jenkins;

namespace Firestarter.Core.Tests.Jenkins;

public class JenkinsGitLabWebhookResolverTests
{
    [Theory]
    [InlineData(
        "https://jenkins.example.com/project/team%2Fservice_master",
        "https://jenkins.example.com",
        "team/service_master")]
    [InlineData(
        "https://jenkins.example.com/ci/project/acme%2Fwidget",
        "https://jenkins.example.com/ci",
        "acme/widget")]
    [InlineData(
        "https://jenkins.example.com/job/folder/job/pipeline/build",
        "https://jenkins.example.com",
        "folder/pipeline")]
    public void TryDeriveJobPathFromHookUrl_matches_base_and_parses_path(string hookUrl, string jenkinsBase, string expected)
    {
        Assert.Equal(expected, JenkinsGitLabWebhookResolver.TryDeriveJobPathFromHookUrl(hookUrl, jenkinsBase));
    }

    [Fact]
    public void TryDeriveJobPathFromHookUrl_returns_null_for_wrong_host()
    {
        Assert.Null(JenkinsGitLabWebhookResolver.TryDeriveJobPathFromHookUrl(
            "https://other.example.com/project/x",
            "https://jenkins.example.com"));
    }

    [Fact]
    public void TryDeriveJobPathFromHookUrl_returns_null_when_not_under_base_path()
    {
        Assert.Null(JenkinsGitLabWebhookResolver.TryDeriveJobPathFromHookUrl(
            "https://jenkins.example.com/project/x",
            "https://jenkins.example.com/ci"));
    }
}
