using Firestarter.Core.Settings;
using Firestarter.Core.Sync;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class SettingsGetHandler(SettingsService settings) : IIpcHandler
{
    readonly SettingsService _settings = settings;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
        => await _settings.GetGitlabConfigAsync(ct);
}

public class SettingsUpdateHandler(SettingsService settings, GitLabSyncService syncService) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly SettingsService _settings = settings;
    readonly GitLabSyncService _syncService = syncService;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        if (payload is null) throw new ArgumentException("payload required");
        var update = payload.Value.Deserialize<GitlabConfigUpdate>(Options)
                     ?? throw new ArgumentException("invalid payload");
        var result = await _settings.UpdateGitlabConfigAsync(update, ct);
        // The 15-second scheduler tick uses an in-memory snapshot of GitlabSettings to avoid reading
        // SQLite each tick — drop it so the next tick reflects the user's change immediately.
        _syncService.InvalidateSettingsCache();
        return result;
    }
}
