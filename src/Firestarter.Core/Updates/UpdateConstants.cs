namespace Firestarter.Core.Updates;

/// <summary>
/// Hard-coded release-feed location. Update <see cref="ManifestUrl"/> to point at the JSON file
/// that holds the latest version + zip download URL. The JSON shape is:
///
/// <code>
/// {
///   "version": "1.4.2",
///   "downloadUrl": "https://github.com/.../v1.4.2/firestarter-x64.zip",
///   "sha256": "optional"
/// }
/// </code>
///
/// Fetched anonymously — the project + file must be publicly readable.
/// </summary>
public static class UpdateConstants
{
    /// <summary>Raw URL to the JSON manifest. Replace with your real GitLab raw file URL.</summary>
    public const string ManifestUrl = "https://raw.githubusercontent.com/Fribergz/firestarter/refs/heads/main/latest.json";

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
}
