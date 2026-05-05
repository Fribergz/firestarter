namespace Firestarter.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Firestarter");

    public static string DatabasePath { get; } = Path.Combine(DataDir, "firestarter.db");
    public static string LogsDir { get; } = Path.Combine(DataDir, "logs");
    public static string RunsDir { get; } = Path.Combine(DataDir, "runs");
    /// <summary>
    /// Default root for cloned projects. The on-disk folder name remains <c>clones</c> so existing
    /// installs keep their workspace intact even though the UI now calls this "Projects root".
    /// </summary>
    public static string DefaultProjectsDir { get; } = Path.Combine(DataDir, "clones");
    public static string DefaultTempDir { get; } = Path.Combine(DataDir, "temp");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(RunsDir);
    }
}
