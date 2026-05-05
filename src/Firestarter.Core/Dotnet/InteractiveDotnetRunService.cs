using Firestarter.Core.Data;
using Firestarter.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Firestarter.Core.Dotnet;

public record InteractiveDotnetStartResult(bool Ok, string? Error, int? Pid);

public partial class InteractiveDotnetRunService(
    IServiceScopeFactory scopeFactory,
    ILogger<InteractiveDotnetRunService> logger)
{
    // Singleton service — holds the live `Process?` across calls — so it can't directly consume scoped
    // deps like FirestarterDbContext or WorkspaceService. We open a scope inside StartAsync instead,
    // mirroring GitLabSyncService.
    readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    readonly Lock _gate = new();
    Process? _process;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false } p ? p.Id : null;
            }
        }
    }

    public async Task<InteractiveDotnetStartResult> StartAsync(
        int projectId,
        string? branch,
        string? statsTargetFromClient,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return new InteractiveDotnetStartResult(false, "Another `dotnet run` is already in progress. Stop it first.", null);
            }
        }

        // Scoped DB + WorkspaceService — short-lived; we only need them to resolve the clone path.
        string repoPath;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FirestarterDbContext>();
            var workspaces = scope.ServiceProvider.GetRequiredService<WorkspaceService>();

            var tracked = await db.Projects.FirstOrDefaultAsync(x => x.Id == projectId, ct)
                ?? throw new InvalidOperationException($"Project {projectId} not found");

            try
            {
                repoPath = await workspaces.EnsureCloneAsync(tracked, branch, ct);
            }
            catch (Exception ex)
            {
                return new InteractiveDotnetStartResult(false, ex.Message, null);
            }
        }

        string? target = ResolveRunTargetPath(repoPath, statsTargetFromClient);
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
        {
            return new InteractiveDotnetStartResult(
                false,
                "Could not find a .sln, .slnx, or .csproj in the repository clone to run (tried `Program.cs` → project, then solution / project heuristics).",
                null);
        }

        if (IsSolutionFilePath(target))
        {
            var fromSln = TryResolveMsbuildProjectFromSolutionFile(target);
            if (string.IsNullOrEmpty(fromSln) || !File.Exists(fromSln))
            {
                return new InteractiveDotnetStartResult(
                    false,
                    "A solution was found, but `dotnet run` could not be mapped to a project. Open the correct .csproj in the solution or set the stats target to a .csproj path.",
                    null);
            }
            target = fromSln;
        }

        var workDir = Path.GetDirectoryName(target) ?? repoPath;
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        psi.ArgumentList.Add("run");
        AddRunLaunchProfileArgs(psi, workDir);
        // `dotnet run` does not use a .sln as a runnable app target; pass `--project` for the actual MSBuild project file.
        if (IsMsbuildProjectFilePath(target))
        {
            psi.ArgumentList.Add("--project");
            psi.ArgumentList.Add(Path.GetFullPath(target));
        }
        else
        {
            psi.ArgumentList.Add(Path.GetFullPath(target));
        }
        psi.WorkingDirectory = workDir;

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            LogStartFailed(logger, ex);
            return new InteractiveDotnetStartResult(false, ex.Message, null);
        }

        if (proc is null)
        {
            return new InteractiveDotnetStartResult(false, "Failed to start the dotnet process.", null);
        }

        lock (_gate)
        {
            _process?.Dispose();
            _process = proc;
        }

        RegisterExitCleanup(proc);

        LogStarted(logger, target, proc.Id);
       
        return new InteractiveDotnetStartResult(true, null, proc.Id);
    }

    void RegisterExitCleanup(Process proc)
    {
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_process, proc)) return;
                _process = null;
            }
            try { proc.Dispose(); } catch { }
        };
    }

    static void AddRunLaunchProfileArgs(ProcessStartInfo psi, string projectDir)
    {
        var name = TrySelectLaunchProfileName(projectDir);
        if (name is not null)
        {
            psi.ArgumentList.Add("--launch-profile");
            psi.ArgumentList.Add(name);
        }
        else
        {
            psi.ArgumentList.Add("--no-launch-profile");
        }
    }

    /// <summary>Resolves a profile from <c>Properties/launchSettings.json</c>: <c>Local</c> first, then <c>Development</c> (name matched case-insensitively; the on-disk name is passed to the CLI).</summary>
    static string? TrySelectLaunchProfileName(string projectDir)
    {
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir)) return null;
        var path = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(path)) return null;
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var local = GetLaunchProfileNameIfPresent(profiles, "Local");
            if (local is not null) return local;
            return GetLaunchProfileNameIfPresent(profiles, "Development");
        }
        catch
        {
            return null;
        }
    }

    static string? GetLaunchProfileNameIfPresent(JsonElement profilesObject, string logicalName)
    {
        foreach (var prop in profilesObject.EnumerateObject())
        {
            if (string.Equals(prop.Name, logicalName, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Name;
            }
        }
        return null;
    }

    public bool TryStop()
    {
        Process? proc;
        lock (_gate)
        {
            proc = _process;
            _process = null;
        }
        if (proc is null) return true;
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            LogKillFailed(logger, ex);
            try { proc.Dispose(); } catch { }
            return false;
        }
        try { proc.Dispose(); } catch { }
        return true;
    }

    /// <summary>Align with extensions/dotnet-review/run.ps1 Resolve-Target, preferring a file name match from the last run.</summary>
    public static string? ResolveRunTargetPath(string repoPath, string? statsTargetFromClient)
    {
        if (!string.IsNullOrWhiteSpace(statsTargetFromClient))
        {
            var name = Path.GetFileName(statsTargetFromClient);
            if (!string.IsNullOrEmpty(name))
            {
                var matches = Directory.GetFiles(
                    repoPath,
                    name,
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                    });
                if (matches.Length > 0) return Path.GetFullPath(matches[0]);
            }
        }

        // Prefer a runnable project implied by a top-level `Program.cs` (common for minimal APIs / host entry).
        var fromEntry = FindProjectFileFromProgramCs(repoPath);
        if (fromEntry is not null) return fromEntry;

        var repoDirName = new DirectoryInfo(repoPath).Name;
        foreach (var pat in new[] { "*.sln", "*.slnx" })
        {
            var top = Directory.GetFiles(repoPath, pat);
            if (top.Length > 0)
            {
                var byName = top.Where(f => string.Equals(
                    Path.GetFileNameWithoutExtension(f),
                    repoDirName,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
                if (byName.Length > 0) return byName[0];
                return top.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).First();
            }
        }

        var cs = Directory.GetFiles(
            repoPath,
            "*.csproj",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 5,
                IgnoreInaccessible = true,
            });
        if (cs.Length > 0) return cs.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).First();
        return null;
    }

    /// <summary>Search for <c>Program.cs</c> (skipping <c>bin</c>/<c>obj</c>), then locate the nearest <c>.csproj</c> in that directory or a parent, up to the repo root.</summary>
    static string? FindProjectFileFromProgramCs(string repoPath)
    {
        repoPath = Path.GetFullPath(repoPath);
        if (!Directory.Exists(repoPath)) return null;

        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        string[] files;
        try
        {
            files = Directory.GetFiles(repoPath, "Program.cs", opts);
        }
        catch
        {
            return null;
        }

        var filtered = files
            .Where(f => !IsUnderIgnoredBuildOrDepsFolder(f, repoPath))
            .ToArray();
        if (filtered.Length == 0) return null;

        // Prefer shallowest path (typical "real" app entry over nested samples), then stable order.
        var best = filtered
            .Select(f => Path.GetFullPath(f))
            .OrderBy(f => DepthRelativeToRepo(f, repoPath))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .First();

        var programDir = Path.GetDirectoryName(best);
        if (string.IsNullOrEmpty(programDir)) return null;
        return FindMsbuildProjectInDirectoryOrAncestors(programDir, repoPath);
    }

    static bool IsUnderIgnoredBuildOrDepsFolder(string fullFilePath, string repoRoot)
    {
        var rel = Path.GetRelativePath(repoRoot, fullFilePath);
        var parts = rel.Split(s_dirSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                p.Equals(".git", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static readonly char[] s_dirSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    static int DepthRelativeToRepo(string filePath, string repoRoot)
    {
        var rel = Path.GetRelativePath(repoRoot, filePath);
        if (string.IsNullOrEmpty(rel) || rel == ".") return 0;
        return rel.Split(s_dirSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    static string? FindMsbuildProjectInDirectoryOrAncestors(string startDir, string repoRoot)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        var d = Path.GetFullPath(startDir);
        for (; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d) ?? string.Empty)
        {
            if (!IsPathUnderOrEqualToRoot(d, repoRoot)) break;
            if (!Directory.Exists(d)) break;

            string[] projs;
            try
            {
                projs = Directory.GetFiles(d, "*.csproj");
            }
            catch
            {
                continue;
            }

            if (projs.Length == 0) continue;
            if (projs.Length == 1) return Path.GetFullPath(projs[0]);
            var nameHint = new DirectoryInfo(d).Name;
            var named = projs
                .Where(f => string.Equals(
                    Path.GetFileNameWithoutExtension(f),
                    nameHint,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (named.Length == 1) return Path.GetFullPath(named[0]);
            return Path.GetFullPath(named.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? projs[0]);
        }
        return null;
    }

    /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or a subdirectory (avoids <c>repo</c> matching <c>repo2</c>).</summary>
    static bool IsPathUnderOrEqualToRoot(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.GetFullPath(root);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
        var r = root.TrimEnd(s_dirSeparators) + Path.DirectorySeparatorChar;
        return path.Length > r.Length && path.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSolutionFilePath(string path)
    {
        var e = Path.GetExtension(path);
        return e.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
               e.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsMsbuildProjectFilePath(string path)
    {
        var e = Path.GetExtension(path);
        return e.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
               e.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
               e.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Prefers the path field of a classic <c>.sln</c> <c>Project(..., "a\b.csproj", { ...</c> line (avoids matching JSON keys).</summary>
    static readonly Regex s_quotedProjectPath = RegexQuotedProjectPath();

    /// <summary>Any double-quoted relative path to an MSBuild project (fallback for <c>.slnx</c> and non-comma terminations).</summary>
    static readonly Regex s_quotedProjectPathLoose = RegexQuotedProjectPathLoose();

    static string? TryResolveMsbuildProjectFromSolutionFile(string solutionPath)
    {
        if (!File.Exists(solutionPath)) return null;
        var slnDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath));
        if (string.IsNullOrEmpty(slnDir)) return null;

        string text;
        try
        {
            text = File.ReadAllText(solutionPath);
        }
        catch
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = new List<string>();
        void AddIfExists(string rel)
        {
            rel = rel.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.IsPathRooted(rel)
                ? Path.GetFullPath(rel)
                : Path.GetFullPath(Path.Combine(slnDir, rel));
            if (File.Exists(full) && seen.Add(full))
            {
                projects.Add(full);
            }
        }

        foreach (Match m in s_quotedProjectPath.Matches(text))
        {
            AddIfExists(m.Groups[1].Value);
        }

        if (projects.Count == 0)
        {
            foreach (Match m in s_quotedProjectPathLoose.Matches(text))
            {
                AddIfExists(m.Groups[1].Value);
            }
        }

        if (projects.Count == 0) return null;
        return SelectBestRunProjectPath(projects);
    }

    static string SelectBestRunProjectPath(List<string> projects)
    {
        if (projects.Count == 1) return projects[0];

        foreach (var p in projects)
        {
            var d = Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(d)) continue;
            if (File.Exists(Path.Combine(d, "Program.cs"))) return p;
        }

        var withoutTest = projects
            .Where(p => !LooksLikeTestProjectName(Path.GetFileNameWithoutExtension(p)))
            .ToList();
        if (withoutTest.Count > 0)
        {
            return withoutTest
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        return projects
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    static bool LooksLikeTestProjectName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("Test.", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Test", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@",\s*""([^""]+\.(cs|vb|fs)proj)""\s*,", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex RegexQuotedProjectPath();
    [GeneratedRegex(@"""([^""\r\n}]+\.(cs|vb|fs)proj)""", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex RegexQuotedProjectPathLoose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to start dotnet run")]
    static partial void LogStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started interactive dotnet run for {Target} (pid {Pid})")]
    static partial void LogStarted(ILogger logger, string target, int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to kill interactive dotnet")]
    static partial void LogKillFailed(ILogger logger, Exception exception);
}
