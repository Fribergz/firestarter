using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Settings;

public record WorkspaceConfig(string ProjectsRoot, string TempRoot);

public class WorkspaceSettings(FirestarterDbContext db)
{
    /// <summary>
    /// KeyValueSettings storage key for the projects-root path. The string itself stays
    /// <c>workspace.clonesRoot</c> (the historical name) so existing rows in
    /// <c>KeyValueSettings</c> from older installs are still picked up after the rename to
    /// "Projects root" in the UI.
    /// </summary>
    public const string ProjectsRootKey = "workspace.clonesRoot";
    public const string TempRootKey = "workspace.tempRoot";

    readonly FirestarterDbContext _db = db;

    public async Task<WorkspaceConfig> GetAsync(CancellationToken ct = default)
    {
        var rows = await _db.KeyValueSettings
            .Where(k => k.Key == ProjectsRootKey || k.Key == TempRootKey)
            .ToDictionaryAsync(k => k.Key, k => k.Value, ct);

        var projects = string.IsNullOrWhiteSpace(rows.GetValueOrDefault(ProjectsRootKey))
            ? AppPaths.DefaultProjectsDir
            : rows[ProjectsRootKey]!;
        var temp = string.IsNullOrWhiteSpace(rows.GetValueOrDefault(TempRootKey))
            ? AppPaths.DefaultTempDir
            : rows[TempRootKey]!;

        return new WorkspaceConfig(projects, temp);
    }

    public async Task<WorkspaceConfig> UpdateAsync(string? projectsRoot, string? tempRoot, CancellationToken ct = default)
    {
        await UpsertAsync(ProjectsRootKey, projectsRoot, ct);
        await UpsertAsync(TempRootKey, tempRoot, ct);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    async Task UpsertAsync(string key, string? value, CancellationToken ct)
    {
        var existing = await _db.KeyValueSettings.FirstOrDefaultAsync(k => k.Key == key, ct);
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (existing is null)
        {
            if (cleaned is null) return;
            _db.KeyValueSettings.Add(new KeyValueSetting { Key = key, Value = cleaned, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.Value = cleaned;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
