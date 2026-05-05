using Firestarter.Core.Settings;
using NGitLab;

namespace Firestarter.Core.GitLab;

public class GitLabClientFactory(SettingsService settings) : IGitLabClientFactory
{
    readonly SettingsService _settings = settings;

    public async Task<IGitLabClient?> CreateAsync(CancellationToken ct = default)
    {
        var cfg = await _settings.GetOrCreateAsync(ct);
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl)) return null;

        var pat = await _settings.GetPatAsync(ct);
        if (string.IsNullOrEmpty(pat)) return null;

        return new GitLabClient(cfg.BaseUrl, pat);
    }
}
