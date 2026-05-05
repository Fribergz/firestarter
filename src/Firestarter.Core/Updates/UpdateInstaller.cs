using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Firestarter.Core.Updates;

/// <summary>
/// Downloads the update zip published in the manifest and spawns a self-contained PowerShell
/// updater that waits for this process to exit, swaps the install directory, and relaunches the
/// app. After spawning the updater, the caller is expected to terminate the host so the swap can proceed.
/// </summary>
public sealed partial class UpdateInstaller(UpdateStatusHub hub, ILogger<UpdateInstaller> logger)
{
    public async Task<UpdateApplyResult> ApplyAsync(CancellationToken ct)
    {
        var snapshot = hub.Snapshot;
        var manifest = snapshot.Latest;
        if (!snapshot.UpdateAvailable || manifest is null)
            return new UpdateApplyResult(false, "No update available.");
        if (snapshot.ApplyInProgress)
            return new UpdateApplyResult(false, "An update is already in progress.");

        hub.Update(s => s with { ApplyInProgress = true, LastError = null });

        try
        {
            var stagingRoot = Path.Combine(AppPaths.DataDir, "update");
            Directory.CreateDirectory(stagingRoot);

            var zipPath = Path.Combine(stagingRoot, $"firestarter-{manifest.Version}.zip");
            await DownloadAsync(manifest.DownloadUrl, zipPath, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                var actual = await Sha256Async(zipPath, ct).ConfigureAwait(false);
                if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Checksum mismatch: expected {manifest.Sha256}, got {actual}.");
            }

            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var exePath = GetCurrentExecutablePath();
            var pid = Environment.ProcessId;
            var scriptPath = Path.Combine(stagingRoot, $"apply-{manifest.Version}.ps1");
            await File.WriteAllTextAsync(scriptPath, BuildUpdaterScript(pid, zipPath, installDir, exePath), ct)
                .ConfigureAwait(false);

            // Spawn detached so it survives our exit.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var _ = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start updater process.");

            LogUpdateScheduled(logger, manifest.Version, scriptPath);
            return new UpdateApplyResult(true, null);
        }
        catch (Exception ex)
        {
            hub.Update(s => s with { ApplyInProgress = false, LastError = ex.Message });
            LogUpdateFailed(logger, ex);
            return new UpdateApplyResult(false, ex.Message);
        }
    }

    static async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(destination);
        await src.CopyToAsync(dst, ct).ConfigureAwait(false);
    }

    static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var bytes = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string GetCurrentExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path)) return path;
        // Fallback: derive from main module.
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Could not resolve current executable path.");
    }

    /// <summary>
    /// Build the PowerShell script that performs the actual swap. It:
    ///   1. Waits for our process to exit (polling Get-Process).
    ///   2. Expands the zip into a temp folder.
    ///   3. Copies its contents over the install directory (force-overwrite).
    ///   4. Relaunches the app.
    /// Path strings are interpolated through .NET escaping; the script itself uses single-quoted
    /// strings so PowerShell does not re-interpret them.
    /// </summary>
    static string BuildUpdaterScript(int pid, string zipPath, string installDir, string exePath)
    {
        var ps = (string s) => "'" + s.Replace("'", "''") + "'";
        var stagingExtract = Path.Combine(Path.GetDirectoryName(zipPath)!, $"extract-{Guid.NewGuid():N}");
        return $$"""
            $ErrorActionPreference = 'Stop'
            $pid_target = {{pid}}
            $zip = {{ps(zipPath)}}
            $install = {{ps(installDir)}}
            $exe = {{ps(exePath)}}
            $extract = {{ps(stagingExtract)}}

            # Wait for the running app to exit (max ~30s).
            for ($i = 0; $i -lt 60; $i++) {
                $running = Get-Process -Id $pid_target -ErrorAction SilentlyContinue
                if (-not $running) { break }
                Start-Sleep -Milliseconds 500
            }

            New-Item -ItemType Directory -Force -Path $extract | Out-Null
            Expand-Archive -Path $zip -DestinationPath $extract -Force

            # If the zip wraps everything in a single root folder, descend into it.
            $children = Get-ChildItem -Path $extract
            if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
                $extract = $children[0].FullName
            }

            Copy-Item -Path (Join-Path $extract '*') -Destination $install -Recurse -Force

            Start-Process -FilePath $exe -WorkingDirectory $install
            """;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update scheduled for version {Version} via {Script}")]
    static partial void LogUpdateScheduled(ILogger logger, string version, string script);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update apply failed")]
    static partial void LogUpdateFailed(ILogger logger, Exception exception);
}

public record UpdateApplyResult(bool Ok, string? Error);
