namespace Firestarter.Core.Data.Entities;

public class Extension
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ScriptPath { get; set; } = string.Empty;
    public string ManifestJson { get; set; } = "{}";
    public int TimeoutSeconds { get; set; } = 600;
    public bool IsEnabled { get; set; } = true;
    /// <summary>JSON object with current values for settings declared in the manifest. Keys = setting names; values are serialized strings (multi-string stored as a JSON array).</summary>
    public string SettingsValuesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
