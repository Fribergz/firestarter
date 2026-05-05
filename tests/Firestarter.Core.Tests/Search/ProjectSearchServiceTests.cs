using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Firestarter.Core.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Tests.Search;

public class ProjectSearchServiceTests : IDisposable
{
    readonly SqliteConnection _connection;
    readonly DbContextOptions<FirestarterDbContext> _options;

    public ProjectSearchServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FirestarterDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new FirestarterDbContext(_options);
        ctx.Database.Migrate();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    FirestarterDbContext CreateContext() => new(_options);

    [Fact]
    public async Task SearchAsync_returns_recent_projects_when_query_is_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var ctx = CreateContext())
        {
            ctx.Projects.AddRange(
                new Project { GitlabId = 1, PathWithNamespace = "group/alpha", Name = "alpha", WebUrl = "u", LastActivityAt = DateTimeOffset.UtcNow.AddDays(-1) },
                new Project { GitlabId = 2, PathWithNamespace = "group/beta", Name = "beta", WebUrl = "u", LastActivityAt = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync(ct);
        }

        await using var search = CreateContext();
        var service = new ProjectSearchService(search);
        var hits = await service.SearchAsync(null, 50, ct);

        Assert.Equal(2, hits.Count);
        Assert.Equal("group/beta", hits[0].PathWithNamespace);
    }

    [Fact]
    public async Task SearchAsync_trigram_matches_substring_across_path_and_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var ctx = CreateContext())
        {
            ctx.Projects.AddRange(
                new Project { GitlabId = 10, PathWithNamespace = "platform/payments-gateway", Name = "payments-gateway", Description = "money movement", WebUrl = "u" },
                new Project { GitlabId = 11, PathWithNamespace = "platform/user-service", Name = "user-service", Description = "accounts", WebUrl = "u" },
                new Project { GitlabId = 12, PathWithNamespace = "tools/repay-audit", Name = "repay-audit", Description = "billing", WebUrl = "u" });
            await ctx.SaveChangesAsync(ct);
        }

        await using var search = CreateContext();
        var service = new ProjectSearchService(search);
        var hits = await service.SearchAsync("pay", 50, ct);

        Assert.Contains(hits, h => h.PathWithNamespace == "platform/payments-gateway");
        Assert.Contains(hits, h => h.PathWithNamespace == "tools/repay-audit");
        Assert.DoesNotContain(hits, h => h.PathWithNamespace == "platform/user-service");
    }

    [Fact]
    public async Task SearchAsync_reflects_updates_through_triggers()
    {
        var ct = TestContext.Current.CancellationToken;
        int id;
        await using (var ctx = CreateContext())
        {
            var p = new Project { GitlabId = 20, PathWithNamespace = "ops/legacy", Name = "legacy", WebUrl = "u" };
            ctx.Projects.Add(p);
            await ctx.SaveChangesAsync(ct);
            id = p.Id;
        }

        await using (var ctx = CreateContext())
        {
            var p = await ctx.Projects.FirstAsync(x => x.Id == id, ct);
            p.PathWithNamespace = "ops/rebranded-service";
            p.Name = "rebranded-service";
            await ctx.SaveChangesAsync(ct);
        }

        await using var search = CreateContext();
        var service = new ProjectSearchService(search);

        var legacyHits = await service.SearchAsync("legacy", 50, ct);
        Assert.DoesNotContain(legacyHits, h => h.Id == id);

        var rebrandedHits = await service.SearchAsync("rebranded", 50, ct);
        Assert.Contains(rebrandedHits, h => h.Id == id);
    }

    [Fact]
    public async Task SearchAsync_excludes_archived_projects()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var ctx = CreateContext())
        {
            ctx.Projects.AddRange(
                new Project { GitlabId = 30, PathWithNamespace = "group/active-widget", Name = "active-widget", WebUrl = "u" },
                new Project { GitlabId = 31, PathWithNamespace = "group/old-widget", Name = "old-widget", WebUrl = "u", Archived = true });
            await ctx.SaveChangesAsync(ct);
        }

        await using var search = CreateContext();
        var service = new ProjectSearchService(search);
        var hits = await service.SearchAsync("widget", 50, ct);

        Assert.Single(hits);
        Assert.Equal("group/active-widget", hits[0].PathWithNamespace);
    }
}
