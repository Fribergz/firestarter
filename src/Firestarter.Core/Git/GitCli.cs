using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Firestarter.Core.Git;

public record GitResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

public partial class GitCli(ILogger<GitCli> logger)
{

    public static bool IsRepository(string path)
        => Directory.Exists(Path.Combine(path, ".git"));

    public Task<GitResult> CloneAsync(string repoUrl, string destination, string? pat, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var args = new List<string>();
        AppendAuthHeader(args, pat);
        args.Add("clone");
        args.Add(repoUrl);
        args.Add(destination);
        return RunAsync(args, workingDirectory: null, pat, ct);
    }

    public Task<GitResult> FetchAllPruneAsync(string repoPath, string? pat, CancellationToken ct)
    {
        var args = new List<string>();
        AppendAuthHeader(args, pat);
        args.Add("fetch");
        args.Add("--all");
        args.Add("--prune");
        return RunAsync(args, repoPath, pat, ct);
    }

    public Task<GitResult> CheckoutAsync(string repoPath, string branch, CancellationToken ct)
    {
        return RunAsync(["checkout", branch], repoPath, pat: null, ct);
    }

    /// <summary>Fast-forward the current branch to match its upstream (after fetch). Uses <c>--ff-only</c> to avoid merge commits.</summary>
    public Task<GitResult> PullFastForwardAsync(string repoPath, string? pat, CancellationToken ct)
    {
        var args = new List<string>();
        AppendAuthHeader(args, pat);
        args.Add("pull");
        args.Add("--ff-only");
        return RunAsync(args, repoPath, pat, ct);
    }

    public Task<GitResult> RevParseHeadAsync(string repoPath, CancellationToken ct)
    {
        return RunAsync(["rev-parse", "HEAD"], repoPath, pat: null, ct);
    }

    async Task<GitResult> RunAsync(IEnumerable<string> args, string? workingDirectory, string? pat, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        if (!string.IsNullOrEmpty(pat))
        {
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = CollectAsync(process.StandardOutput, stdout, ct);
        var stderrTask = CollectAsync(process.StandardError, stderr, ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        var result = new GitResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        if (!result.Success)
        {
            LogGitFailed(logger,
                string.Join(' ', Redact(psi.ArgumentList)),
                workingDirectory ?? string.Empty,
                result.ExitCode,
                result.Stderr.Trim());
        }
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "git {Args} in {Cwd} exited {Code}: {Stderr}")]
    static partial void LogGitFailed(ILogger logger, string args, string cwd, int code, string stderr);

    static async Task CollectAsync(StreamReader reader, StringBuilder sink, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (!ct.IsCancellationRequested)
        {
            var n = await reader.ReadAsync(buffer, ct);
            if (n == 0) break;
            sink.Append(buffer, 0, n);
        }
    }

    static void AppendAuthHeader(List<string> args, string? pat)
    {
        if (string.IsNullOrEmpty(pat)) return;
        args.Add("-c");
        args.Add($"http.extraHeader=PRIVATE-TOKEN: {pat}");
    }

    static IEnumerable<string> Redact(IList<string> args)
    {
        foreach (var a in args)
        {
            yield return a.StartsWith("http.extraHeader=", StringComparison.Ordinal)
                ? "http.extraHeader=***"
                : a;
        }
    }
}
