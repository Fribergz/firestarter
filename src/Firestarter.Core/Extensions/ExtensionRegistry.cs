using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Firestarter.Core.Extensions;

public record ExtensionView(
    int Id,
    string Name,
    string? Description,
    string ScriptPath,
    int TimeoutSeconds,
    bool IsEnabled,
    IReadOnlyList<ExtensionParameter> Parameters,
    IReadOnlyList<ExtensionTarget> Targets,
    IReadOnlyList<ExtensionSetting> SettingsSchema,
    IReadOnlyDictionary<string, string> SettingsValues);

public record ExtensionScanResult(int Added, int Updated, int Removed, IReadOnlyList<string> Errors);

public class ExtensionRegistry(FirestarterDbContext db, ILogger<ExtensionRegistry> logger)
{
    public const string ExtensionsRootKey = "extensions.root";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly string[] ManifestPatterns = ["extension.yaml", "extension.yml"];

    readonly FirestarterDbContext _db = db;
    readonly ILogger<ExtensionRegistry> _logger = logger;

    public async Task<string?> GetRootAsync(CancellationToken ct)
    {
        var row = await _db.KeyValueSettings.FirstOrDefaultAsync(k => k.Key == ExtensionsRootKey, ct);
        return string.IsNullOrWhiteSpace(row?.Value) ? AppPaths.DefaultExtensionsDir : row!.Value;
    }

    public async Task SetRootAsync(string? path, CancellationToken ct)
    {
        var row = await _db.KeyValueSettings.FirstOrDefaultAsync(k => k.Key == ExtensionsRootKey, ct);
        var cleaned = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        if (row is null)
        {
            if (cleaned is null) return;
            _db.KeyValueSettings.Add(new KeyValueSetting { Key = ExtensionsRootKey, Value = cleaned, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = cleaned;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExtensionView>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.Extensions.OrderBy(p => p.Name).ToListAsync(ct);
        return [.. rows.Select(Project)];
    }

    public async Task<ExtensionView?> GetAsync(int id, CancellationToken ct)
    {
        var row = await _db.Extensions.FirstOrDefaultAsync(p => p.Id == id, ct);
        return row is null ? null : Project(row);
    }

    public async Task<ExtensionView?> SetEnabledAsync(int id, bool enabled, CancellationToken ct)
    {
        var row = await _db.Extensions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return null;
        if (row.IsEnabled != enabled)
        {
            row.IsEnabled = enabled;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Project(row);
    }

    public async Task<ExtensionScanResult> ScanAsync(CancellationToken ct)
    {
        var root = await GetRootAsync(ct);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new ExtensionScanResult(0, 0, 0, [$"Extensions root not set or missing: {root}"]);

        var manifests = ManifestPatterns
            .SelectMany(p => Directory.EnumerateFiles(root, p, SearchOption.AllDirectories))
            .ToList();

        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0;

        foreach (var manifestPath in manifests)
        {
            ExtensionManifest manifest;
            try
            {
                manifest = await ExtensionManifestLoader.LoadAsync(manifestPath, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{manifestPath}: {ex.Message}");
                continue;
            }

            var dir = Path.GetDirectoryName(manifestPath)!;
            var scriptPath = Path.GetFullPath(Path.Combine(dir, manifest.Entrypoint));
            if (!File.Exists(scriptPath))
            {
                errors.Add($"{manifestPath}: entrypoint {scriptPath} not found");
                continue;
            }

            if (!seen.Add(manifest.Name))
            {
                errors.Add($"Duplicate extension name '{manifest.Name}' at {manifestPath}");
                continue;
            }

            var manifestJson = JsonSerializer.Serialize(manifest, Json);
            var existing = await _db.Extensions.FirstOrDefaultAsync(p => p.Name == manifest.Name, ct);
            if (existing is null)
            {
                _db.Extensions.Add(new Extension
                {
                    Name = manifest.Name,
                    Description = manifest.Description,
                    ScriptPath = scriptPath,
                    ManifestJson = manifestJson,
                    TimeoutSeconds = manifest.TimeoutSeconds,
                    IsEnabled = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
                added++;
            }
            else
            {
                existing.Description = manifest.Description;
                existing.ScriptPath = scriptPath;
                existing.ManifestJson = manifestJson;
                existing.TimeoutSeconds = manifest.TimeoutSeconds;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                updated++;
            }
        }

        var stale = await _db.Extensions.Where(p => !seen.Contains(p.Name)).ToListAsync(ct);
        _db.Extensions.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        return new ExtensionScanResult(added, updated, stale.Count, errors);
    }

    public async Task<ExtensionView?> SetSettingsAsync(int id, IReadOnlyDictionary<string, string?>? updates, CancellationToken ct)
    {
        updates ??= new Dictionary<string, string?>();
        var row = await _db.Extensions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return null;

        var (_, _, schema) = TryReadManifest(row.ManifestJson);
        var schemaByName = schema.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var current = TryReadValues(row.SettingsValuesJson);
        var merged = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in updates)
        {
            if (!schemaByName.TryGetValue(kv.Key, out var setting)) continue;
            if (kv.Value is null)
            {
                merged.Remove(setting.Name);
                continue;
            }
            merged[setting.Name] = NormalizeValue(setting, kv.Value);
        }

        // Drop entries no longer in the schema.
        foreach (var key in merged.Keys.ToList())
        {
            if (!schemaByName.ContainsKey(key)) merged.Remove(key);
        }

        row.SettingsValuesJson = JsonSerializer.Serialize(merged, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Project(row);
    }

    /// <summary>Resolves the effective value map for a run: declared defaults overlaid by stored values, then run-time params take precedence (handled by caller).</summary>
    public static IReadOnlyDictionary<string, string> ResolveSettingValues(string manifestJson, string settingsValuesJson)
    {
        var (_, _, schema) = TryReadManifest(manifestJson);
        var stored = TryReadValues(settingsValuesJson);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in schema)
        {
            if (stored.TryGetValue(s.Name, out var v)) result[s.Name] = v;
            else if (!string.IsNullOrEmpty(s.Default)) result[s.Name] = NormalizeValue(s, s.Default);
        }
        return result;
    }

    static string NormalizeValue(ExtensionSetting setting, string raw)
    {
        switch (setting.Type)
        {
            case ExtensionManifestLoader.SettingTypeBoolean:
                return string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            case ExtensionManifestLoader.SettingTypeMultiString:
                // Accept either a comma-separated string or a JSON array; always store as comma-joined.
                if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
                var trimmed = raw.Trim();
                if (trimmed.StartsWith('['))
                {
                    try
                    {
                        var arr = JsonSerializer.Deserialize<string[]>(trimmed, Json) ?? [];
                        return string.Join(",", arr.Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0));
                    }
                    catch { /* fall through */ }
                }
                return string.Join(",", trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            default:
                return raw ?? string.Empty;
        }
    }

    static ExtensionView Project(Extension p)
    {
        var (parameters, targets, settings) = TryReadManifest(p.ManifestJson);
        var values = TryReadValues(p.SettingsValuesJson);
        // Surface declared defaults for unset values so the UI shows the resolved state.
        var resolved = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var s in settings)
        {
            if (!resolved.ContainsKey(s.Name) && !string.IsNullOrEmpty(s.Default))
                resolved[s.Name] = NormalizeValue(s, s.Default);
        }
        return new ExtensionView(p.Id, p.Name, p.Description, p.ScriptPath, p.TimeoutSeconds, p.IsEnabled, parameters, targets, settings, resolved);
    }

    static (IReadOnlyList<ExtensionParameter> Parameters, IReadOnlyList<ExtensionTarget> Targets, IReadOnlyList<ExtensionSetting> Settings) TryReadManifest(string manifestJson)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(manifestJson, Json);
            return (manifest?.Parameters ?? [], manifest?.Targets ?? [], manifest?.Settings ?? []);
        }
        catch
        {
            return ([], [], []);
        }
    }

    static Dictionary<string, string> TryReadValues(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
