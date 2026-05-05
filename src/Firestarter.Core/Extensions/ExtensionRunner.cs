using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Firestarter.Core.Settings;
using Firestarter.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Firestarter.Core.Extensions;

public record ExtensionRunRequest(int ExtensionId, int ProjectId, string? Branch, IReadOnlyDictionary<string, string>? Parameters);

public partial class ExtensionRunner(
    FirestarterDbContext db,
    WorkspaceSettings workspace,
    WorkspaceService workspaces,
    ILogger<ExtensionRunner> logger)
{
    readonly FirestarterDbContext _db = db;
    readonly WorkspaceSettings _workspace = workspace;
    readonly WorkspaceService _workspaces = workspaces;

    public async Task<ExtensionRun> RunAsync(ExtensionRunRequest request, CancellationToken ct)
    {
        var extension = await _db.Extensions.FirstOrDefaultAsync(p => p.Id == request.ExtensionId, ct)
            ?? throw new InvalidOperationException($"Extension {request.ExtensionId} not found");
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            ?? throw new InvalidOperationException($"Project {request.ProjectId} not found");

        if (!extension.IsEnabled)
            throw new InvalidOperationException($"Extension '{extension.Name}' is disabled");

        if (!File.Exists(extension.ScriptPath))
            throw new InvalidOperationException($"Extension script missing: {extension.ScriptPath}");

        var workspace = await _workspace.GetAsync(ct);
        var runId = Guid.NewGuid().ToString("N");
        var runDir = Path.Combine(AppPaths.RunsDir, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{runId[..8]}");
        Directory.CreateDirectory(runDir);

        var stdoutPath = Path.Combine(runDir, "stdout.log");
        var stderrPath = Path.Combine(runDir, "stderr.log");

        var branch = request.Branch ?? project.DefaultBranch;
        var repoPath = await _workspaces.EnsureCloneAsync(project, branch, ct);

        var run = new ExtensionRun
        {
            ExtensionId = extension.Id,
            ProjectId = project.Id,
            BranchName = branch ?? string.Empty,
            Status = ExtensionRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            WorkingDirectory = repoPath,
            StdoutPath = stdoutPath,
            StderrPath = stderrPath,
        };
        _db.ExtensionRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        try
        {
            var settings = ExtensionRegistry.ResolveSettingValues(extension.ManifestJson, extension.SettingsValuesJson);
            var (exitCode, stdout, timedOut) = await ExecuteAsync(extension, repoPath, stdoutPath, stderrPath, request.Parameters, settings, ct);
            run.ExitCode = exitCode;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.StatsJson = StatsExtractor.Extract(stdout);

            run.Status = timedOut
                ? ExtensionRunStatus.TimedOut
                : exitCode == 0 ? ExtensionRunStatus.Succeeded : ExtensionRunStatus.Failed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Status = ExtensionRunStatus.Cancelled;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorMessage = "Cancelled";
            throw;
        }
        catch (Exception ex)
        {
            run.Status = ExtensionRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.ErrorMessage = ex.Message;
            LogRunFailed(logger, extension.Name, ex);
        }
        finally
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        return run;
    }

    static async Task<(int ExitCode, string Stdout, bool TimedOut)> ExecuteAsync(
        Extension extension,
        string repoPath,
        string stdoutPath,
        string stderrPath,
        IReadOnlyDictionary<string, string>? parameters,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoPath,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(extension.ScriptPath);

        psi.Environment["FIRESTARTER_REPO"] = repoPath;
        psi.Environment["FIRESTARTER_EXTENSION"] = extension.Name;
        foreach (var kv in settings)
        {
            psi.Environment["FIRESTARTER_SETTING_" + Sanitize(kv.Key)] = kv.Value ?? string.Empty;
        }
        if (parameters is not null)
        {
            foreach (var kv in parameters)
            {
                var key = "FIRESTARTER_PARAM_" + Sanitize(kv.Key);
                psi.Environment[key] = kv.Value ?? string.Empty;
            }
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start pwsh (is PowerShell 7+ on PATH?)");

        await using var stdoutFile = new StreamWriter(stdoutPath, append: false);
        await using var stderrFile = new StreamWriter(stderrPath, append: false);
        var stdoutBuffer = new StringBuilder();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(extension.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var stdoutTask = PumpAsync(process.StandardOutput, stdoutFile, stdoutBuffer, linkedCts.Token);
        var stderrTask = PumpAsync(process.StandardError, stderrFile, sink: null, linkedCts.Token);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (timeoutCts.IsCancellationRequested) timedOut = true;
        }

        try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        return (exitCode, stdoutBuffer.ToString(), timedOut);
    }

    static async Task PumpAsync(StreamReader reader, TextWriter file, StringBuilder? sink, CancellationToken ct)
    {
        var buf = new char[4096];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = await reader.ReadAsync(buf, ct); }
            catch (OperationCanceledException) { return; }
            if (n == 0) return;
            await file.WriteAsync(buf.AsMemory(0, n), ct);
            await file.FlushAsync(ct);
            sink?.Append(buf, 0, n);
        }
    }

    static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray();
        return new string(chars);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Extension {Extension} run failed")]
    static partial void LogRunFailed(ILogger logger, string extension, Exception exception);
}
