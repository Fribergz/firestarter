using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Firestarter.Core.Updates;

/// <summary>
/// Hourly background poller that fetches the JSON manifest at <see cref="UpdateConstants.ManifestUrl"/>
/// and writes the result into <see cref="UpdateStatusHub"/>. Runs anonymously (no auth) — the manifest
/// and zip URL are expected to be publicly readable.
/// </summary>
public sealed partial class UpdateCheckService(UpdateStatusHub hub, ILogger<UpdateCheckService> logger) : BackgroundService
{
    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(UpdateConstants.StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            try { await Task.Delay(UpdateConstants.CheckInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = UpdateConstants.HttpTimeout };
            using var resp = await http.GetAsync(UpdateConstants.ManifestUrl, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
                throw new InvalidOperationException("Manifest is missing version or downloadUrl.");

            hub.Update(s => s with
            {
                Latest = manifest,
                UpdateAvailable = UpdateStatusHub.IsNewer(manifest.Version, s.CurrentVersion),
                LastError = null,
                LastCheckedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            hub.Update(s => s with
            {
                LastError = ex.Message,
                LastCheckedAt = DateTimeOffset.UtcNow,
            });
            LogPollFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update manifest poll failed")]
    static partial void LogPollFailed(ILogger logger, Exception exception);
}
