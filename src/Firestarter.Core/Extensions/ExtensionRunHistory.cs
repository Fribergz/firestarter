using Firestarter.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Extensions;

public record ExtensionRunSummary(
    int Id,
    int ExtensionId,
    string ExtensionName,
    int ProjectId,
    string ProjectPath,
    string Branch,
    string? CommitSha,
    string Status,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? StatsJson,
    string? ErrorMessage);

public record ExtensionRunLog(int Id, string? Stdout, string? Stderr, bool StdoutTruncated, bool StderrTruncated);

public class ExtensionRunHistory(FirestarterDbContext db)
{
    const int LogTailBytes = 256 * 1024;

    readonly FirestarterDbContext _db = db;

    public Task<List<ExtensionRunSummary>> ListAsync(int? extensionId, int? projectId, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _db.ExtensionRuns
            .AsNoTracking()
            .Include(r => r.Extension)
            .Include(r => r.Project)
            .OrderByDescending(r => r.StartedAt)
            .AsQueryable();

        if (extensionId is int ex) query = query.Where(r => r.ExtensionId == ex);
        if (projectId is int p) query = query.Where(r => r.ProjectId == p);

        return query
            .Take(take)
            .Select(r => new ExtensionRunSummary(
                r.Id,
                r.ExtensionId,
                r.Extension != null ? r.Extension.Name : "(deleted)",
                r.ProjectId,
                r.Project != null ? r.Project.PathWithNamespace : "(deleted)",
                r.BranchName,
                r.CommitSha,
                r.Status.ToString(),
                r.ExitCode,
                r.StartedAt,
                r.FinishedAt,
                r.StatsJson,
                r.ErrorMessage))
            .ToListAsync(ct);
    }

    public async Task<ExtensionRunLog?> GetLogAsync(int runId, CancellationToken ct)
    {
        var row = await _db.ExtensionRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => new { r.Id, r.StdoutPath, r.StderrPath })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        var (stdout, stdoutTrunc) = ReadTail(row.StdoutPath);
        var (stderr, stderrTrunc) = ReadTail(row.StderrPath);
        return new ExtensionRunLog(row.Id, stdout, stderr, stdoutTrunc, stderrTrunc);
    }

    static (string? Content, bool Truncated) ReadTail(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (null, false);
        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0) return (string.Empty, false);
            if (info.Length <= LogTailBytes)
                return (File.ReadAllText(path), false);

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(-LogTailBytes, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            reader.ReadLine();
            return (reader.ReadToEnd(), true);
        }
        catch
        {
            return (null, false);
        }
    }
}
