using Firestarter.Core.Data;
using Firestarter.Core.Security;
using Firestarter.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Tests.Settings;

class FakeCredentialStore : ICredentialStore
{
    readonly Dictionary<string, string> _store = [];
    public string? Get(string name) => _store.TryGetValue(name, out var v) ? v : null;
    public void Set(string name, string secret) => _store[name] = secret;
    public void Delete(string name) => _store.Remove(name);
    public bool Exists(string name) => _store.ContainsKey(name);
}

public class SettingsServiceTests
{
    static FirestarterDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<FirestarterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FirestarterDbContext(opts);
    }

    [Fact]
    public async Task Get_without_config_creates_default_row_and_reports_no_pat()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var svc = new SettingsService(db, new FakeCredentialStore());

        var cfg = await svc.GetGitlabConfigAsync(ct);

        Assert.Null(cfg.BaseUrl);
        Assert.False(cfg.HasPat);
        Assert.Equal(300, cfg.SyncIntervalSeconds);
    }

    [Fact]
    public async Task Update_stores_pat_in_credential_store_only()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var creds = new FakeCredentialStore();
        var svc = new SettingsService(db, creds);

        await svc.UpdateGitlabConfigAsync(new GitlabConfigUpdate
        {
            BaseUrl = "https://gitlab.example.com",
            Pat = "glpat-test",
            ClearPat = false,
            SyncIntervalSeconds = 120,
        }, ct);

        var row = await db.GitlabSettings.SingleAsync(ct);
        Assert.Equal("https://gitlab.example.com", row.BaseUrl);
        Assert.Null(row.GetType().GetProperty("Pat")); // PAT is never a property on the row
        Assert.Equal("glpat-test", await svc.GetPatAsync(ct));
        Assert.True(creds.Exists(row.PatCredentialName!));
    }

    [Fact]
    public async Task Update_with_empty_pat_preserves_existing_credential()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var creds = new FakeCredentialStore();
        var svc = new SettingsService(db, creds);

        await svc.UpdateGitlabConfigAsync(new GitlabConfigUpdate
        {
            BaseUrl = "https://gitlab.example.com",
            Pat = "glpat-initial",
            SyncIntervalSeconds = 300,
        }, ct);

        await svc.UpdateGitlabConfigAsync(new GitlabConfigUpdate
        {
            BaseUrl = "https://gitlab.example.com",
            Pat = null,
            ClearPat = false,
            SyncIntervalSeconds = 300,
        }, ct);

        Assert.Equal("glpat-initial", await svc.GetPatAsync(ct));
    }

    [Fact]
    public async Task Update_with_clearPat_removes_credential()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var creds = new FakeCredentialStore();
        var svc = new SettingsService(db, creds);

        await svc.UpdateGitlabConfigAsync(new GitlabConfigUpdate
        {
            BaseUrl = "https://gitlab.example.com",
            Pat = "glpat-initial",
            SyncIntervalSeconds = 300,
        }, ct);

        await svc.UpdateGitlabConfigAsync(new GitlabConfigUpdate
        {
            BaseUrl = "https://gitlab.example.com",
            ClearPat = true,
            SyncIntervalSeconds = 300,
        }, ct);

        Assert.Null(await svc.GetPatAsync(ct));
    }

    [Fact]
    public async Task Update_clamps_intervals_to_minimums()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var svc = new SettingsService(db, new FakeCredentialStore());

        var cfg = await svc.UpdateGitlabConfigAsync(new GitlabConfigUpdate
        {
            SyncIntervalSeconds = 5,
        }, ct);

        Assert.Equal(30, cfg.SyncIntervalSeconds);
    }

    [Fact]
    public async Task Jenkins_get_creates_default_row_and_reports_no_token()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var svc = new SettingsService(db, new FakeCredentialStore());

        var cfg = await svc.GetJenkinsConfigAsync(ct);

        Assert.Null(cfg.BaseUrl);
        Assert.Null(cfg.Username);
        Assert.False(cfg.HasApiToken);
    }

    [Fact]
    public async Task Jenkins_update_stores_api_token_in_credential_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewDb();
        var creds = new FakeCredentialStore();
        var svc = new SettingsService(db, creds);

        await svc.UpdateJenkinsConfigAsync(new JenkinsConfigUpdate
        {
            BaseUrl = "https://jenkins.example.com",
            Username = "svc",
            ApiToken = "abc123",
            ClearApiToken = false,
        }, ct);

        var row = await db.JenkinsSettings.SingleAsync(ct);
        Assert.Equal("https://jenkins.example.com", row.BaseUrl);
        Assert.Equal("svc", row.Username);
        Assert.Equal("abc123", await svc.GetJenkinsApiTokenAsync(ct));
        Assert.True(creds.Exists(row.ApiTokenCredentialName!));
    }
}
