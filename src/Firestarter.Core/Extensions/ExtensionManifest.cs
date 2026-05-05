using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Firestarter.Core.Extensions;

public record ExtensionParameter(string Name, string? Description, string? Default, bool Required);

public record ExtensionTarget(string View, string? Label);

/// <summary>
/// Per-extension persistent setting declared in the manifest. Edited from the GUI; injected into runs as
/// <c>FIRESTARTER_SETTING_&lt;KEY&gt;</c> env vars (multi-string serialized as comma-separated).
/// </summary>
public record ExtensionSetting(
    string Name,
    string? Label,
    string? Description,
    /// <summary>One of: <c>string</c>, <c>boolean</c>, <c>multi-string</c>.</summary>
    string Type,
    string? Default);

public record ExtensionManifest(
    string Name,
    string? Description,
    string Entrypoint,
    int TimeoutSeconds,
    IReadOnlyList<ExtensionParameter> Parameters,
    IReadOnlyList<ExtensionTarget> Targets,
    IReadOnlyList<ExtensionSetting> Settings);

public static class ExtensionManifestLoader
{
    public const string ViewProject = "project";
    public const string ViewMergeRequest = "merge-request";

    static readonly HashSet<string> KnownViews = new(StringComparer.OrdinalIgnoreCase)
    {
        ViewProject,
        ViewMergeRequest,
    };

    public const string SettingTypeString = "string";
    public const string SettingTypeBoolean = "boolean";
    public const string SettingTypeMultiString = "multi-string";

    static readonly HashSet<string> KnownSettingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        SettingTypeString,
        SettingTypeBoolean,
        SettingTypeMultiString,
    };

    static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static async Task<ExtensionManifest> LoadAsync(string manifestPath, CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(manifestPath, ct);
        var raw = Yaml.Deserialize<RawManifest?>(content)
            ?? throw new InvalidOperationException($"Empty manifest: {manifestPath}");

        if (string.IsNullOrWhiteSpace(raw.Name))
            throw new InvalidOperationException($"Manifest missing name: {manifestPath}");
        if (string.IsNullOrWhiteSpace(raw.Entrypoint))
            throw new InvalidOperationException($"Manifest missing entrypoint: {manifestPath}");

        var targets = (raw.Targets ?? [])
            .Select(t => new ExtensionTarget(
                (t.View ?? string.Empty).Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(t.Label) ? null : t.Label!.Trim()))
            .Where(t => KnownViews.Contains(t.View))
            .ToList();

        var settings = (raw.Settings ?? [])
            .Select(s =>
            {
                var type = (s.Type ?? SettingTypeString).Trim().ToLowerInvariant();
                if (!KnownSettingTypes.Contains(type)) type = SettingTypeString;
                return new ExtensionSetting(
                    (s.Name ?? string.Empty).Trim(),
                    string.IsNullOrWhiteSpace(s.Label) ? null : s.Label!.Trim(),
                    s.Description,
                    type,
                    s.Default);
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();

        return new ExtensionManifest(
            raw.Name!,
            raw.Description,
            raw.Entrypoint!,
            raw.TimeoutSeconds <= 0 ? 600 : raw.TimeoutSeconds,
            [.. (raw.Parameters ?? [])
                .Select(p => new ExtensionParameter(
                    p.Name ?? string.Empty,
                    p.Description,
                    p.Default,
                    p.Required))
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))],
            targets,
            settings);
    }

    class RawManifest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Entrypoint { get; set; }
        public int TimeoutSeconds { get; set; }
        public List<RawParameter>? Parameters { get; set; }
        public List<RawTarget>? Targets { get; set; }
        public List<RawSetting>? Settings { get; set; }
    }

    class RawSetting
    {
        public string? Name { get; set; }
        public string? Label { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public string? Default { get; set; }
    }

    class RawParameter
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Default { get; set; }
        public bool Required { get; set; }
    }

    class RawTarget
    {
        public string? View { get; set; }
        public string? Label { get; set; }
    }
}
