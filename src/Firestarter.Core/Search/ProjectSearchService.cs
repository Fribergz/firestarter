using Firestarter.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Search;

public record ProjectSearchHit(
    int Id,
    long GitlabId,
    string PathWithNamespace,
    string Name,
    string? Description,
    string? DefaultBranch,
    string WebUrl,
    bool Archived,
    DateTimeOffset? LastActivityAt);

public class ProjectSearchService(FirestarterDbContext db)
{
    readonly FirestarterDbContext _db = db;

    public async Task<IReadOnlyList<ProjectSearchHit>> SearchAsync(string? query, int limit = 25, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var trimmed = (query ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return await _db.Projects
                .AsNoTracking()
                .Where(p => !p.Archived && p.LastVisitedAt != null)
                .OrderByDescending(p => p.LastVisitedAt)
                .Take(limit)
                .Select(p => new ProjectSearchHit(
                    p.Id, p.GitlabId, p.PathWithNamespace, p.Name, p.Description,
                    p.DefaultBranch, p.WebUrl, p.Archived, p.LastActivityAt))
                .ToListAsync(ct);
        }

        var match = BuildMatchExpression(trimmed);
        if (match is null)
        {
            return await _db.Projects
                .AsNoTracking()
                .Where(p => !p.Archived)
                .OrderByDescending(p => p.LastActivityAt)
                .Take(limit)
                .Select(p => new ProjectSearchHit(
                    p.Id, p.GitlabId, p.PathWithNamespace, p.Name, p.Description,
                    p.DefaultBranch, p.WebUrl, p.Archived, p.LastActivityAt))
                .ToListAsync(ct);
        }

        const string sql = @"
SELECT p.*
FROM Projects AS p
JOIN ProjectFts AS f ON f.rowid = p.Id
WHERE ProjectFts MATCH {0}
  AND p.Archived = 0
ORDER BY bm25(ProjectFts, 3.0, 2.0, 1.0)
LIMIT {1}";

        var projects = await _db.Projects
            .FromSqlRaw(sql, match, limit)
            .AsNoTracking()
            .ToListAsync(ct);

        return [.. projects.Select(p => new ProjectSearchHit(
                p.Id, p.GitlabId, p.PathWithNamespace, p.Name, p.Description,
                p.DefaultBranch, p.WebUrl, p.Archived, p.LastActivityAt))];
    }

    static string? BuildMatchExpression(string input)
    {
        var tokens = input
            .Split([' ', '\t', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .Select(EscapeFtsTerm)
            .Where(t => t is not null)
            .ToArray();

        if (tokens.Length == 0) return null;
        return string.Join(" ", tokens);
    }

    static string? EscapeFtsTerm(string term)
    {
        var cleaned = term.Replace("\"", string.Empty);
        if (cleaned.Length == 0) return null;
        return "\"" + cleaned + "\"";
    }
}
