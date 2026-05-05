using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Settings;

public record UiConfig(string Theme);

public class UiSettings(FirestarterDbContext db)
{
    public const string ThemeKey = "ui.theme";
    public const string DefaultTheme = "ember";
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "ember", "graphite", "obsidian" };

    readonly FirestarterDbContext _db = db;

    public async Task<UiConfig> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.KeyValueSettings.FirstOrDefaultAsync(k => k.Key == ThemeKey, ct);
        var theme = row?.Value;
        if (string.IsNullOrWhiteSpace(theme) || !Allowed.Contains(theme)) theme = DefaultTheme;
        return new UiConfig(theme.ToLowerInvariant());
    }

    public async Task<UiConfig> SetThemeAsync(string? theme, CancellationToken ct = default)
    {
        var cleaned = (theme ?? string.Empty).Trim().ToLowerInvariant();
        if (!Allowed.Contains(cleaned)) cleaned = DefaultTheme;

        var existing = await _db.KeyValueSettings.FirstOrDefaultAsync(k => k.Key == ThemeKey, ct);
        if (existing is null)
            _db.KeyValueSettings.Add(new KeyValueSetting { Key = ThemeKey, Value = cleaned, UpdatedAt = DateTimeOffset.UtcNow });
        else
        {
            existing.Value = cleaned;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return new UiConfig(cleaned);
    }
}
