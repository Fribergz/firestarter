namespace Firestarter.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Firestarter");

    public static string DatabasePath { get; } = Path.Combine(DataDir, "firestarter.db");
    public static string LogsDir { get; } = Path.Combine(DataDir, "logs");
    public static string RunsDir { get; } = Path.Combine(DataDir, "runs");
    /// <summary>Default root for cloned projects (sibling of extensions/, runs/, temp/, the SQLite DB).</summary>
    public static string DefaultProjectsDir { get; } = Path.Combine(DataDir, "projects");
    /// <summary>Default scratch dir for transient working copies (sibling of projects/, runs/, the SQLite DB).</summary>
    public static string DefaultTempDir { get; } = Path.Combine(DataDir, "temp");
    /// <summary>Default root for installed extension scripts (sibling of projects/, runs/, the SQLite DB).</summary>
    public static string DefaultExtensionsDir { get; } = Path.Combine(DataDir, "extensions");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(RunsDir);
    }
}
