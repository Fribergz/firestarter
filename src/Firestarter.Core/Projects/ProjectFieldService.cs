using Firestarter.Core.Data;
using Firestarter.Core.Jenkins;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Projects;

public class ProjectFieldService(FirestarterDbContext db)
{
    readonly FirestarterDbContext _db = db;

    public async Task<bool> SetJenkinsJobPathAsync(int projectId, string? jenkinsJobPath, CancellationToken ct = default)
    {
        var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == projectId, ct);
        if (p is null) return false;
        var trimmed = string.IsNullOrWhiteSpace(jenkinsJobPath)
            ? null
            : JenkinsRestClient.NormalizeJobPath(jenkinsJobPath);
        if (trimmed is not null)
        {
            if (trimmed.Length == 0) trimmed = null;
            else if (trimmed.Length > 512) trimmed = trimmed[..512];
        }
        var previousPath = p.JenkinsJobPath;
        p.JenkinsJobPath = trimmed;
        if (!string.Equals(previousPath, trimmed, StringComparison.Ordinal))
        {
            p.JenkinsPipelinesCacheJson = null;
            p.JenkinsPipelinesCachedAt = null;
            p.JenkinsPipelinesCacheRunTake = null;
            p.JenkinsPipelinesCacheComplete = false;
        }
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
