using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Firestarter.Core.Ide;

public class IdeLauncher(FirestarterDbContext db)
{
    readonly FirestarterDbContext _db = db;

    public Task<List<IdeRegistration>> ListAsync(CancellationToken ct = default)
        => _db.IdeRegistrations.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<IdeRegistration> UpsertAsync(IdeRegistration input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Name is required");
        if (string.IsNullOrWhiteSpace(input.ExecutablePath)) throw new ArgumentException("ExecutablePath is required");

        var existing = input.Id > 0
            ? await _db.IdeRegistrations.FirstOrDefaultAsync(x => x.Id == input.Id, ct)
            : await _db.IdeRegistrations.FirstOrDefaultAsync(x => x.Name == input.Name, ct);

        if (existing is null)
        {
            existing = new IdeRegistration();
            _db.IdeRegistrations.Add(existing);
        }

        existing.Name = input.Name.Trim();
        existing.ExecutablePath = input.ExecutablePath.Trim();
        existing.ArgTemplate = string.IsNullOrWhiteSpace(input.ArgTemplate) ? "\"{path}\"" : input.ArgTemplate.Trim();
        existing.IsDefault = input.IsDefault;

        if (existing.IsDefault)
        {
            await _db.IdeRegistrations
                .Where(x => x.Id != existing.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.IdeRegistrations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return;
        _db.IdeRegistrations.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IdeRegistration?> GetAsync(int? id, CancellationToken ct = default)
    {
        if (id is int pinned)
            return await _db.IdeRegistrations.FirstOrDefaultAsync(x => x.Id == pinned, ct);
        return await _db.IdeRegistrations.FirstOrDefaultAsync(x => x.IsDefault, ct)
            ?? await _db.IdeRegistrations.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
    }

    public static int Launch(IdeRegistration ide, string projectPath)
    {
        if (!File.Exists(ide.ExecutablePath))
            throw new FileNotFoundException($"IDE executable not found: {ide.ExecutablePath}");

        var template = string.IsNullOrWhiteSpace(ide.ArgTemplate) ? "\"{path}\"" : ide.ArgTemplate;
        var solution = ResolveSolutionPath(projectPath) ?? projectPath;
        var args = template
            .Replace("{solution}", solution)
            .Replace("{path}", projectPath);

        var psi = new ProcessStartInfo
        {
            FileName = ide.ExecutablePath,
            Arguments = args,
            UseShellExecute = true,
            WorkingDirectory = projectPath,
        };

        var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start IDE");
        return proc.Id;
    }

    static string? ResolveSolutionPath(string repoPath)
    {
        if (!Directory.Exists(repoPath)) return null;

        var solutions = Directory.EnumerateFiles(repoPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(repoPath, "*.slnx", SearchOption.TopDirectoryOnly))
            .ToArray();

        if (solutions.Length == 0) return null;
        if (solutions.Length == 1) return solutions[0];

        var dirName = new DirectoryInfo(repoPath).Name;
        var namedMatch = solutions.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), dirName, StringComparison.OrdinalIgnoreCase));
        if (namedMatch is not null) return namedMatch;

        return solutions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
    }
}
