using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Firestarter.Core.Git;
using Firestarter.Core.Ide;
using Firestarter.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Firestarter.Core.Workspaces;

public record OpenProjectResult(string RepositoryPath, string Branch, string? CommitSha, string IdeName, int IdeProcessId);

public partial class WorkspaceService(
    FirestarterDbContext db,
    SettingsService settings,
    WorkspaceSettings workspace,
    GitCli git,
    IdeLauncher ide,
    ILogger<WorkspaceService> logger)
{
    readonly FirestarterDbContext _db = db;
    readonly SettingsService _settings = settings;
    readonly WorkspaceSettings _workspace = workspace;
    readonly GitCli _git = git;
    readonly IdeLauncher _ide = ide;

    public async Task<string> EnsureCloneAsync(Project project, string? branch, CancellationToken ct)
    {
        var workspace = await _workspace.GetAsync(ct);
        var repoPath = Path.Combine(workspace.ProjectsRoot, project.PathWithNamespace.Replace('/', Path.DirectorySeparatorChar));
        var pat = await _settings.GetPatAsync(ct);
        var cloneUrl = PickCloneUrl(project)
            ?? throw new InvalidOperationException("Project has no HTTP clone URL");

        var alreadyOnDisk = GitCli.IsRepository(repoPath);
        if (alreadyOnDisk)
        {
            var fetch = await _git.FetchAllPruneAsync(repoPath, pat, ct);
            if (!fetch.Success)
                throw new InvalidOperationException($"git fetch failed: {fetch.Stderr.Trim()}");
        }
        else
        {
            var clone = await _git.CloneAsync(cloneUrl, repoPath, pat, ct);
            if (!clone.Success)
                throw new InvalidOperationException($"git clone failed: {clone.Stderr.Trim()}");
        }

        var target = string.IsNullOrWhiteSpace(branch) ? project.DefaultBranch : branch;
        if (!string.IsNullOrWhiteSpace(target))
        {
            var checkout = await _git.CheckoutAsync(repoPath, target!, ct);
            if (!checkout.Success)
            {
                LogCheckoutFailed(logger, target!, checkout.Stderr.Trim());
            }
            else if (alreadyOnDisk)
            {
                var pull = await _git.PullFastForwardAsync(repoPath, pat, ct);
                if (!pull.Success)
                    LogPullAfterCheckoutFailed(logger, pull.Stderr.Trim());
            }
        }
        else if (alreadyOnDisk)
        {
            var pull = await _git.PullFastForwardAsync(repoPath, pat, ct);
            if (!pull.Success)
                LogPullFailed(logger, pull.Stderr.Trim());
        }

        return repoPath;
    }

    public async Task<OpenProjectResult> OpenInIdeAsync(int projectId, string? branch, int? ideId, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found");

        var ide = await _ide.GetAsync(ideId, ct)
            ?? throw new InvalidOperationException("No IDE registered. Configure one in Settings.");

        var repoPath = await EnsureCloneAsync(project, branch, ct);
        var head = await _git.RevParseHeadAsync(repoPath, ct);
        var sha = head.Success ? head.Stdout.Trim() : null;

        var pid = IdeLauncher.Launch(ide, repoPath);
        return new OpenProjectResult(repoPath, branch ?? project.DefaultBranch ?? string.Empty, sha, ide.Name, pid);
    }

    public async Task<string> OpenFolderInExplorerAsync(int projectId, string? branch, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found");
        var repoPath = await EnsureCloneAsync(project, branch, ct);
        if (!Directory.Exists(repoPath))
            throw new InvalidOperationException($"Repository path not found: {repoPath}");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { repoPath },
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start Explorer: {ex.Message}", ex);
        }
        return repoPath;
    }

    public async Task<string> OpenFolderInTerminalAsync(int projectId, string? branch, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException($"Project {projectId} not found");
        var repoPath = await EnsureCloneAsync(project, branch, ct);
        if (!Directory.Exists(repoPath))
            throw new InvalidOperationException($"Repository path not found: {repoPath}");
        StartTerminalInDirectory(repoPath);
        return repoPath;
    }

    static void StartTerminalInDirectory(string directoryPath)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wt = Path.Combine(localAppData, "Microsoft", "Windows Terminal", "wt.exe");
        if (File.Exists(wt))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = wt,
                ArgumentList = { "-d", directoryPath },
                UseShellExecute = true,
            });
            return;
        }
        // Host Windows Terminal in Program Files
        var wtPf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Terminal", "wt.exe");
        if (File.Exists(wtPf))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = wtPf,
                ArgumentList = { "-d", directoryPath },
                UseShellExecute = true,
            });
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList = { "/k" },
            WorkingDirectory = directoryPath,
            UseShellExecute = true,
        });
    }

    static string? PickCloneUrl(Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.HttpUrlToRepo)) return project.HttpUrlToRepo;
        if (!string.IsNullOrWhiteSpace(project.SshUrlToRepo)) return project.SshUrlToRepo;
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "git checkout {Branch} failed: {Stderr}")]
    static partial void LogCheckoutFailed(ILogger logger, string branch, string stderr);

    [LoggerMessage(Level = LogLevel.Warning, Message = "git pull --ff-only failed after checkout: {Stderr}")]
    static partial void LogPullAfterCheckoutFailed(ILogger logger, string stderr);

    [LoggerMessage(Level = LogLevel.Warning, Message = "git pull --ff-only failed: {Stderr}")]
    static partial void LogPullFailed(ILogger logger, string stderr);
}
