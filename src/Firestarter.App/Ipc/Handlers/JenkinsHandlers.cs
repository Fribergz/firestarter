using Firestarter.Core.Jenkins;
using Firestarter.Core.Projects;
using Firestarter.Core.Settings;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

public class JenkinsGetHandler(SettingsService settings) : IIpcHandler
{
    readonly SettingsService _settings = settings;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        return await _settings.GetJenkinsConfigAsync(ct);
    }
}

public class JenkinsUpdateHandler(SettingsService settings) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly SettingsService _settings = settings;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        if (payload is null) throw new ArgumentException("payload required");
        var update = payload.Value.Deserialize<JenkinsConfigUpdate>(Options)
                     ?? throw new ArgumentException("invalid payload");
        return await _settings.UpdateJenkinsConfigAsync(update, ct);
    }
}

public class JenkinsTestHandler(SettingsService settings) : IIpcHandler
{
    readonly SettingsService _settings = settings;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        var view = await _settings.GetJenkinsConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(view.BaseUrl)) throw new InvalidOperationException("Jenkins base URL is not set.");
        if (string.IsNullOrWhiteSpace(view.Username)) throw new InvalidOperationException("Jenkins username is not set.");
        if (!view.HasApiToken) throw new InvalidOperationException("Jenkins API token is not stored.");

        var token = await _settings.GetJenkinsApiTokenAsync(ct);
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Jenkins API token is missing from the credential store.");

        var err = await JenkinsConnectionProbe.TestAsync(view.BaseUrl, view.Username, token, ct);
        if (err is not null) throw new InvalidOperationException(err);
        return new { ok = true };
    }
}

public class JenkinsSetProjectJobHandler(ProjectFieldService fields, ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly ProjectFieldService _fields = fields;
    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        if (payload is null) throw new ArgumentException("payload required");
        var dto = payload.Value.Deserialize<Payload>(Options) ?? throw new ArgumentException("invalid payload");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");
        var ok = await _fields.SetJenkinsJobPathAsync(dto.ProjectId.Value, dto.JobPath, ct);
        if (!ok) throw new InvalidOperationException("Project not found.");
        var project = await _read.GetAsync(dto.ProjectId.Value, ct);
        return new { ok = true, project };
    }

    class Payload
    {
        public int? ProjectId { get; set; }
        public string? JobPath { get; set; }
    }
}

public class JenkinsBuildStatusHandler(SettingsService settings, ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly SettingsService _settings = settings;
    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        if (payload is null) throw new ArgumentException("payload required");
        var dto = payload.Value.Deserialize<StatusPayload>(Options) ?? throw new ArgumentException("invalid payload");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");

        var jenkins = await _settings.GetJenkinsConfigAsync(ct);
        var token = await _settings.GetJenkinsApiTokenAsync(ct);
        var project = await _read.GetAsync(dto.ProjectId.Value, ct)
            ?? throw new InvalidOperationException("Project not found.");

        if (string.IsNullOrWhiteSpace(jenkins.BaseUrl)
            || string.IsNullOrWhiteSpace(jenkins.Username)
            || string.IsNullOrEmpty(token)
            || string.IsNullOrWhiteSpace(project.JenkinsJobPath))
        {
            return new
            {
                configured = false,
                jobUrl = (string?)null,
                lastBuild = (object?)null,
                error = (string?)null,
            };
        }

        var path = project.JenkinsJobPath!;
        var jobUrl = JenkinsRestClient.BuildJobPrefixUrl(jenkins.BaseUrl, path);
        try
        {
            using var http = JenkinsRestClient.CreateClient(jenkins.Username, token);
            var last = await JenkinsRestClient.GetLastBuildAsync(http, jenkins.BaseUrl, path, ct);
            return new
            {
                configured = true,
                jobUrl,
                lastBuild = last is null
                    ? null
                    : (object)new { last.Number, last.Url, last.Building, last.Result },
                error = (string?)null,
            };
        }
        catch (Exception ex)
        {
            return new
            {
                configured = true,
                jobUrl,
                lastBuild = (object?)null,
                error = ex.Message,
            };
        }
    }

    class StatusPayload { public int? ProjectId { get; set; } }
}

public class JenkinsBuildTriggerHandler(SettingsService settings, ProjectReadService read) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    readonly SettingsService _settings = settings;
    readonly ProjectReadService _read = read;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        if (payload is null) throw new ArgumentException("payload required");
        var dto = payload.Value.Deserialize<TriggerPayload>(Options) ?? throw new ArgumentException("invalid payload");
        if (dto.ProjectId is null) throw new ArgumentException("projectId required");

        var jenkins = await _settings.GetJenkinsConfigAsync(ct);
        var token = await _settings.GetJenkinsApiTokenAsync(ct);
        var project = await _read.GetAsync(dto.ProjectId.Value, ct)
            ?? throw new InvalidOperationException("Project not found.");
        if (string.IsNullOrWhiteSpace(jenkins.BaseUrl)
            || string.IsNullOrWhiteSpace(jenkins.Username)
            || string.IsNullOrEmpty(token)
            || string.IsNullOrWhiteSpace(project.JenkinsJobPath))
            throw new InvalidOperationException("Jenkins or project job path is not configured.");

        Dictionary<string, string>? parameters = null;
        if (!string.IsNullOrWhiteSpace(dto.Branch) || dto.MrIid is not null)
        {
            parameters = [];
            if (!string.IsNullOrWhiteSpace(dto.Branch))
                parameters["BRANCH"] = dto.Branch.Trim();
            if (dto.MrIid is not null)
                parameters["MR_IID"] = dto.MrIid.Value.ToString();
        }

        using var http = JenkinsRestClient.CreateClient(jenkins.Username, token);
        await JenkinsRestClient.TriggerBuildAsync(http, jenkins.BaseUrl, project.JenkinsJobPath!, parameters, ct);
        return new { ok = true };
    }

    class TriggerPayload
    {
        public int? ProjectId { get; set; }
        public string? Branch { get; set; }
        public long? MrIid { get; set; }
    }
}
