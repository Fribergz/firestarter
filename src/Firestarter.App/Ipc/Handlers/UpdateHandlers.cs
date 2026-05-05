using System.Text.Json;
using Firestarter.Core.Updates;

namespace Firestarter.App.Ipc.Handlers;

public class UpdateStatusHandler(UpdateStatusHub hub) : IIpcHandler
{
    readonly UpdateStatusHub _hub = hub;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var s = _hub.Snapshot;
        return Task.FromResult<object?>(new
        {
            currentVersion = s.CurrentVersion,
            updateAvailable = s.UpdateAvailable,
            latestVersion = s.Latest?.Version,
            downloadUrl = s.Latest?.DownloadUrl,
            lastError = s.LastError,
            lastCheckedAt = s.LastCheckedAt,
            applyInProgress = s.ApplyInProgress,
        });
    }
}

/// <summary>
/// Triggers the update flow: downloads the new zip, spawns the PowerShell updater, then asks the
/// host window to close so the swap can proceed. The browser-side `applyUpdate()` returns once the
/// updater is launched — the OS process exit happens shortly after as the host shuts down.
/// </summary>
public class UpdateApplyHandler(UpdateInstaller installer, WindowAccessor accessor) : IIpcHandler
{
    readonly UpdateInstaller _installer = installer;
    readonly WindowAccessor _accessor = accessor;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var result = await _installer.ApplyAsync(ct);
        if (result.Ok)
        {
            // Schedule a window close on the dispatcher so the IPC reply has a chance to flush
            // before the process tears down.
            try
            {
                var w = _accessor.Window;
                w.Invoke(w.Close);
            }
            catch
            {
                /* best effort — the updater script will wait until the process exits anyway */
            }
        }
        return new { ok = result.Ok, error = result.Error };
    }
}
