using Firestarter.Core.Data;
using Firestarter.Core.Jenkins;
using Firestarter.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Projects;

/// <summary>Loads recent Jenkins builds (live API), persists snapshots to the DB, and falls back to the last snapshot on failure.</summary>
public class ProjectPipelineService(FirestarterDbContext db, SettingsService settings)
{
    readonly FirestarterDbContext _db = db;
    readonly SettingsService _settings = settings;

    /// <summary>All non-archived projects that have a non-empty deserialized pipeline cache (DB only).</summary>
    public async Task<IReadOnlyList<ProjectCachedPipelinesRow>> ListCachedSnapshotsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Projects.AsNoTracking()
            .Where(p =>
                !p.Archived
                && p.JenkinsPipelinesCacheJson != null
                && p.JenkinsPipelinesCacheJson != "")
            .OrderBy(p => p.PathWithNamespace)
            .Select(p => new
            {
                p.Id,
                p.PathWithNamespace,
                p.JenkinsPipelinesCachedAt,
                p.JenkinsPipelinesCacheJson,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var list = new List<ProjectCachedPipelinesRow>();
        foreach (var r in rows)
        {
            var pipes = JenkinsPipelineSnapshotJson.Deserialize(r.JenkinsPipelinesCacheJson);
            if (pipes is not { Count: > 0 }) continue;
            list.Add(new ProjectCachedPipelinesRow(r.Id, r.PathWithNamespace, r.JenkinsPipelinesCachedAt, pipes));
        }

        return list;
    }

    public async Task<(
        IReadOnlyList<JenkinsBuildSummaryDto> Pipelines,
        bool JenkinsConfigured,
        string? Error,
        DateTimeOffset? CachedAt)> ListRecentAsync(
        int projectId,
        int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);

        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
            .ConfigureAwait(false);
        if (project is null)
            return ([], false, "Project not found.", null);

        var jenkins = await _settings.GetJenkinsConfigAsync(ct).ConfigureAwait(false);
        var token = await _settings.GetJenkinsApiTokenAsync(ct).ConfigureAwait(false);
        var jenkinsConfigured =
            !string.IsNullOrWhiteSpace(jenkins.BaseUrl)
            && !string.IsNullOrWhiteSpace(jenkins.Username)
            && !string.IsNullOrEmpty(token)
            && !string.IsNullOrWhiteSpace(project.JenkinsJobPath);

        var cachedFull = JenkinsPipelineSnapshotJson.Deserialize(project.JenkinsPipelinesCacheJson);
        IReadOnlyList<JenkinsBuildSummaryDto>? cached = cachedFull;
        if (cached is not null && cached.Count > take)
            cached = [.. cached.Take(take)];
        var cachedAt = project.JenkinsPipelinesCachedAt;

        if (string.IsNullOrWhiteSpace(jenkins.BaseUrl)
            || string.IsNullOrWhiteSpace(jenkins.Username)
            || string.IsNullOrEmpty(token))
            return (cached ?? [], false, null, cachedAt);

        if (string.IsNullOrWhiteSpace(project.JenkinsJobPath))
            return ([], true, "Set a Jenkins job path for this project to load builds.", null);

        try
        {
            using var http = JenkinsRestClient.CreateClient(jenkins.Username!, token);

            if (cachedFull is { Count: > 0 })
            {
                var last = await JenkinsRestClient.GetLastBuildAsync(
                    http,
                    jenkins.BaseUrl!,
                    project.JenkinsJobPath,
                    ct).ConfigureAwait(false);
                if (JenkinsPipelineCacheReusePolicy.CanShortCircuit(
                        cachedFull,
                        take,
                        project.JenkinsPipelinesCacheRunTake,
                        project.JenkinsPipelinesCacheComplete,
                        last))
                    return (cached ?? cachedFull, true, null, cachedAt);
            }

            var builds = await JenkinsRestClient.ListRecentBuildsAsync(
                http,
                jenkins.BaseUrl!,
                project.JenkinsJobPath,
                take,
                ct).ConfigureAwait(false);
            await PersistCacheAsync(projectId, builds, take, ct).ConfigureAwait(false);
            var at = DateTimeOffset.UtcNow;
            return (builds, true, null, at);
        }
        catch (Exception ex)
        {
            if (cached is not null)
                return (cached, true, ex.Message, cachedAt);
            return ([], true, ex.Message, null);
        }
    }

    async Task PersistCacheAsync(
        int projectId,
        IReadOnlyList<JenkinsBuildSummaryDto> builds,
        int requestedTake,
        CancellationToken ct)
    {
        var tracked = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct).ConfigureAwait(false);
        if (tracked is null) return;
        tracked.JenkinsPipelinesCacheJson = JenkinsPipelineSnapshotJson.Serialize(builds);
        tracked.JenkinsPipelinesCachedAt = DateTimeOffset.UtcNow;
        tracked.JenkinsPipelinesCacheRunTake = requestedTake;
        tracked.JenkinsPipelinesCacheComplete = builds.Count < requestedTake;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
