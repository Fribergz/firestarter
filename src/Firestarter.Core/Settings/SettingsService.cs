using Firestarter.Core.Data;
using Firestarter.Core.Data.Entities;
using Firestarter.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace Firestarter.Core.Settings;

public record GitlabConfigView(
    string? BaseUrl,
    bool HasPat,
    int SyncIntervalSeconds,
    string? CurrentUsername);

public class GitlabConfigUpdate
{
    public string? BaseUrl { get; set; }
    public string? Pat { get; set; }
    public bool ClearPat { get; set; }
    public int SyncIntervalSeconds { get; set; } = 300;
}

public record JenkinsConfigView(string? BaseUrl, string? Username, bool HasApiToken);

public class JenkinsConfigUpdate
{
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? ApiToken { get; set; }
    public bool ClearApiToken { get; set; }
}

public class SettingsService(FirestarterDbContext db, ICredentialStore credentials)
{
    const string PatCredentialName = "Firestarter:GitLab:PAT";
    const string JenkinsApiTokenCredentialName = "Firestarter:Jenkins:ApiToken";

    readonly FirestarterDbContext _db = db;
    readonly ICredentialStore _credentials = credentials;

    public async Task<GitlabSettings> GetOrCreateAsync(CancellationToken ct = default)
    {
        var s = await _db.GitlabSettings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new GitlabSettings
            {
                Id = 1,
                PatCredentialName = PatCredentialName,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.GitlabSettings.Add(s);
            await _db.SaveChangesAsync(ct);
        }
        return s;
    }

    public async Task<GitlabConfigView> GetGitlabConfigAsync(CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        return new GitlabConfigView(
            s.BaseUrl,
            _credentials.Exists(s.PatCredentialName ?? PatCredentialName),
            s.SyncIntervalSeconds,
            s.CurrentUsername);
    }

    public async Task<GitlabConfigView> UpdateGitlabConfigAsync(GitlabConfigUpdate update, CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);

        s.BaseUrl = string.IsNullOrWhiteSpace(update.BaseUrl) ? null : update.BaseUrl.TrimEnd('/');
        s.SyncIntervalSeconds = Math.Max(30, update.SyncIntervalSeconds);
        s.UpdatedAt = DateTimeOffset.UtcNow;
        s.PatCredentialName ??= PatCredentialName;

        if (update.ClearPat)
        {
            _credentials.Delete(s.PatCredentialName);
        }
        else if (!string.IsNullOrEmpty(update.Pat))
        {
            _credentials.Set(s.PatCredentialName, update.Pat);
        }

        await _db.SaveChangesAsync(ct);
        return await GetGitlabConfigAsync(ct);
    }

    public async Task<string?> GetPatAsync(CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        return _credentials.Get(s.PatCredentialName ?? PatCredentialName);
    }

    public async Task SetCurrentUsernameAsync(string? username, CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        if (s.CurrentUsername == username) return;
        s.CurrentUsername = username;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<JenkinsSettings> GetOrCreateJenkinsAsync(CancellationToken ct = default)
    {
        var s = await _db.JenkinsSettings.FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new JenkinsSettings
            {
                Id = 1,
                ApiTokenCredentialName = JenkinsApiTokenCredentialName,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _db.JenkinsSettings.Add(s);
            await _db.SaveChangesAsync(ct);
        }
        return s;
    }

    public async Task<JenkinsConfigView> GetJenkinsConfigAsync(CancellationToken ct = default)
    {
        var s = await GetOrCreateJenkinsAsync(ct);
        return new JenkinsConfigView(
            s.BaseUrl,
            s.Username,
            _credentials.Exists(s.ApiTokenCredentialName ?? JenkinsApiTokenCredentialName));
    }

    public async Task<JenkinsConfigView> UpdateJenkinsConfigAsync(JenkinsConfigUpdate update, CancellationToken ct = default)
    {
        var s = await GetOrCreateJenkinsAsync(ct);
        s.BaseUrl = string.IsNullOrWhiteSpace(update.BaseUrl) ? null : update.BaseUrl.TrimEnd('/');
        s.Username = string.IsNullOrWhiteSpace(update.Username) ? null : update.Username.Trim();
        s.UpdatedAt = DateTimeOffset.UtcNow;
        s.ApiTokenCredentialName ??= JenkinsApiTokenCredentialName;

        if (update.ClearApiToken)
            _credentials.Delete(s.ApiTokenCredentialName);
        else if (!string.IsNullOrEmpty(update.ApiToken))
            _credentials.Set(s.ApiTokenCredentialName, update.ApiToken);

        await _db.SaveChangesAsync(ct);
        return await GetJenkinsConfigAsync(ct);
    }

    public async Task<string?> GetJenkinsApiTokenAsync(CancellationToken ct = default)
    {
        var s = await GetOrCreateJenkinsAsync(ct);
        return _credentials.Get(s.ApiTokenCredentialName ?? JenkinsApiTokenCredentialName);
    }
}
