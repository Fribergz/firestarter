using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Firestarter.Core.Jenkins;

public record JenkinsLastBuildDto(int Number, string Url, bool Building, string? Result);

public record JenkinsPipelineStepDto(string Name, string Status);

/// <summary>Summary of a Jenkins build for listing recent runs (e.g. “pipelines” in the UI).</summary>
public record JenkinsBuildSummaryDto(
    int Number,
    string Status,
    string Url,
    string? Sha,
    string? VersionTag,
    IReadOnlyList<JenkinsPipelineStepDto>? Steps,
    /// <summary>First stage/step that failed, was aborted, or was unstable (pipeline order).</summary>
    string? FailedOn,
    DateTimeOffset? StartedAt);

public static class JenkinsRestClient
{
    /// <summary>
    /// Webhooks and some docs use <c>.../project/segment1/segment2</c> while Jenkins pages and the REST API use
    /// <c>.../job/segment1/job/segment2</c>. This returns the slash-separated path we store and pass to the API
    /// (<c>segment1/segment2</c>), optionally after stripping a leading <c>project</c> segment or parsing a pasted URL.
    /// </summary>
    public static string NormalizeJobPath(string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath)) return "";
        var s = jobPath.Trim();
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            s = uri.AbsolutePath.Trim('/');

        var segments = s.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length > 0 && segments[0].Equals("project", StringComparison.OrdinalIgnoreCase))
            segments = segments[1..];

        return string.Join("/", segments);
    }

    public static string BuildJobPrefixUrl(string baseUrl, string jobPath)
    {
        var root = baseUrl.TrimEnd('/');
        var normalized = NormalizeJobPath(jobPath);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) throw new ArgumentException("jobPath is empty.", nameof(jobPath));
        var jobPart = string.Join("/", segments.Select(s => $"job/{Uri.EscapeDataString(s)}"));
        return $"{root}/{jobPart}";
    }

    public static HttpClient CreateClient(string username, string apiToken)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public static async Task<JenkinsLastBuildDto?> GetLastBuildAsync(
        HttpClient http,
        string baseUrl,
        string jobPath,
        CancellationToken ct = default)
    {
        var jobUrl = BuildJobPrefixUrl(baseUrl, jobPath);
        var url = $"{jobUrl}/api/json?tree=lastBuild[number,url,building,result]";
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Jenkins status HTTP {(int)response.StatusCode}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("lastBuild", out var lb) || lb.ValueKind == JsonValueKind.Null)
            return null;

        var number = lb.GetProperty("number").GetInt32();
        var buildUrl = lb.GetProperty("url").GetString() ?? "";
        var building = lb.TryGetProperty("building", out var b) && b.GetBoolean();
        string? result = null;
        if (lb.TryGetProperty("result", out var r) && r.ValueKind != JsonValueKind.Null)
            result = r.GetString();
        return new JenkinsLastBuildDto(number, buildUrl, building, result);
    }

    /// <summary>Recent builds for a job, newest first (Jenkins <c>builds</c> array order).</summary>
    public static async Task<IReadOnlyList<JenkinsBuildSummaryDto>> ListRecentBuildsAsync(
        HttpClient http,
        string baseUrl,
        string jobPath,
        int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);
        var jobUrl = BuildJobPrefixUrl(baseUrl, jobPath);
        var tree = "builds[number,url,building,result,timestamp,changeSet[items[commitId]]]";
        var url = $"{jobUrl}/api/json?tree={Uri.EscapeDataString(tree)}";
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Jenkins builds HTTP {(int)response.StatusCode}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("builds", out var builds) || builds.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<JenkinsBuildSummaryDto>();
        foreach (var b in builds.EnumerateArray())
        {
            if (list.Count >= take) break;
            if (b.ValueKind != JsonValueKind.Object) continue;

            var number = b.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt32()
                : 0;
            var buildUrl = b.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var building = b.TryGetProperty("building", out var bl) && bl.GetBoolean();
            string? result = null;
            if (b.TryGetProperty("result", out var r) && r.ValueKind is JsonValueKind.String)
                result = r.GetString();

            DateTimeOffset? started = null;
            if (b.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number)
            {
                var ms = ts.GetInt64();
                if (ms > 0)
                    started = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            string? sha = null;
            if (b.TryGetProperty("changeSet", out var cs) && cs.ValueKind == JsonValueKind.Object
                && cs.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("commitId", out var cid) && cid.ValueKind == JsonValueKind.String)
                    {
                        var s = cid.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            sha = s;
                            break;
                        }
                    }
                }
            }

            var status = building ? "RUNNING" : (result ?? "PENDING");
            list.Add(new JenkinsBuildSummaryDto(number, status, buildUrl, sha, null, null, null, started));
        }

        for (var i = 0; i < list.Count; i++)
            list[i] = await EnrichBuildSummaryAsync(http, list[i], ct).ConfigureAwait(false);

        return list;
    }

    /// <summary>Loads build parameters (version/tag) and Pipeline <c>wfapi/describe</c> stages/steps when available.</summary>
    public static async Task<JenkinsBuildSummaryDto> EnrichBuildSummaryAsync(
        HttpClient http,
        JenkinsBuildSummaryDto build,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(build.Url))
            return build;

        var baseUrl = build.Url.TrimEnd('/') + "/";
        string? versionTag = null;
        List<JenkinsPipelineStepDto>? steps = null;
        string? failedOn = null;

        try
        {
            var paramUrl = $"{baseUrl}api/json?tree=actions[parameters[name,value]]";
            using var resp = await http.GetAsync(paramUrl, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                versionTag = TryExtractVersionTagFromBuildApi(doc.RootElement);
            }
        }
        catch
        {
            /* optional */
        }

        try
        {
            var wfUrl = $"{baseUrl}wfapi/describe";
            using var resp = await http.GetAsync(wfUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return build with { VersionTag = versionTag ?? build.VersionTag };

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (TryParseWfapiSteps(doc.RootElement, out var parsedSteps, out var parsedFail))
            {
                steps = parsedSteps;
                failedOn = parsedFail;
            }
        }
        catch
        {
            /* non-pipeline job or plugin missing */
        }

        return build with
        {
            VersionTag = versionTag,
            Steps = steps,
            FailedOn = failedOn,
        };
    }

    internal static string? TryExtractVersionTagFromBuildApi(JsonElement root)
    {
        if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            return null;

        string? best = null;
        foreach (var action in actions.EnumerateArray())
        {
            if (!action.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var p in parameters.EnumerateArray())
            {
                if (!p.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameEl.GetString() ?? "";
                if (!ParameterNameMightBeVersion(name))
                    continue;
                if (!p.TryGetProperty("value", out var valueEl))
                    continue;
                var val = JsonValueToDisplayString(valueEl);
                if (!string.IsNullOrWhiteSpace(val))
                    best = val.Trim();
            }
        }

        return best;
    }

    static bool ParameterNameMightBeVersion(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Equals("TAG_NAME", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GIT_TAG", StringComparison.OrdinalIgnoreCase)
            || name.Equals("VERSION", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TAG", StringComparison.OrdinalIgnoreCase)
            || name.Equals("RELEASE_VERSION", StringComparison.OrdinalIgnoreCase)
            || name.Equals("IMAGE_TAG", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DOCKER_TAG", StringComparison.OrdinalIgnoreCase);
    }

    static string? JsonValueToDisplayString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    internal static bool TryParseWfapiSteps(
        JsonElement root,
        out List<JenkinsPipelineStepDto> steps,
        out string? failedOn)
    {
        steps = [];
        failedOn = null;
        if (!root.TryGetProperty("stages", out var stagesEl) || stagesEl.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var stage in stagesEl.EnumerateArray())
        {
            var stageName = stage.TryGetProperty("name", out var sn) && sn.ValueKind == JsonValueKind.String
                ? sn.GetString() ?? ""
                : "";

            var stageStatus = "UNKNOWN";
            if (stage.TryGetProperty("status", out var ss))
                stageStatus = ReadJenkinsStatusString(ss) ?? "UNKNOWN";

            if (stage.TryGetProperty("stageFlowNodes", out var flow) && flow.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in flow.EnumerateArray())
                    WalkFlowNode(node, stageName, steps, ref failedOn);
            }
            else if (!string.IsNullOrEmpty(stageName))
            {
                steps.Add(new JenkinsPipelineStepDto(stageName, stageStatus));
                NoteFailure(ref failedOn, stageName, stageStatus);
            }
        }

        return steps.Count > 0;
    }

    static void WalkFlowNode(JsonElement node, string stageName, List<JenkinsPipelineStepDto> steps, ref string? failedOn)
    {
        var nodeName = node.TryGetProperty("name", out var nn) && nn.ValueKind == JsonValueKind.String
            ? nn.GetString() ?? ""
            : "";
        var display = string.IsNullOrEmpty(stageName) ? nodeName : $"{stageName} · {nodeName}";
        if (string.IsNullOrWhiteSpace(display))
            display = stageName;

        var stat = "UNKNOWN";
        if (node.TryGetProperty("status", out var st))
            stat = ReadJenkinsStatusString(st) ?? stat;
        else if (node.TryGetProperty("result", out var rs))
            stat = ReadJenkinsStatusString(rs) ?? stat;

        steps.Add(new JenkinsPipelineStepDto(display.Trim(), stat));
        NoteFailure(ref failedOn, display.Trim(), stat);

        if (node.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
        {
            foreach (var ch in kids.EnumerateArray())
                WalkFlowNode(ch, stageName, steps, ref failedOn);
        }
    }

    static string? ReadJenkinsStatusString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        _ => null,
    };

    static void NoteFailure(ref string? failedOn, string stepLabel, string status)
    {
        if (failedOn is not null || string.IsNullOrWhiteSpace(stepLabel)) return;
        if (IsJenkinsFailureStatus(status))
            failedOn = stepLabel;
    }

    static bool IsJenkinsFailureStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Equals("FAILURE", StringComparison.OrdinalIgnoreCase)
            || status.Equals("ABORTED", StringComparison.OrdinalIgnoreCase)
            || status.Equals("NOT_BUILT", StringComparison.OrdinalIgnoreCase)
            || status.Equals("UNSTABLE", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task TriggerBuildAsync(
        HttpClient http,
        string baseUrl,
        string jobPath,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken ct = default)
    {
        var jobUrl = BuildJobPrefixUrl(baseUrl, jobPath);
        var root = baseUrl.TrimEnd('/');

        string? crumbField = null;
        string? crumbValue = null;
        try
        {
            using var crumbResp = await http.GetAsync($"{root}/crumbIssuer/api/json", ct).ConfigureAwait(false);
            if (crumbResp.IsSuccessStatusCode)
            {
                await using var stream = await crumbResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.TryGetProperty("crumbRequestField", out var f))
                    crumbField = f.GetString();
                if (doc.RootElement.TryGetProperty("crumb", out var c))
                    crumbValue = c.GetString();
            }
        }
        catch
        {
            /* optional crumb */
        }

        var hasParams = parameters is { Count: > 0 };
        var target = hasParams ? $"{jobUrl}/buildWithParameters" : $"{jobUrl}/build";
        using var request = new HttpRequestMessage(HttpMethod.Post, target);
        if (!string.IsNullOrEmpty(crumbField) && !string.IsNullOrEmpty(crumbValue))
            request.Headers.TryAddWithoutValidation(crumbField, crumbValue);

        if (hasParams)
        {
            var pairs = string.Join("&", parameters!.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            request.Content = new StringContent(pairs, Encoding.UTF8, "application/x-www-form-urlencoded");
        }
        else
            request.Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode
            || response.StatusCode == System.Net.HttpStatusCode.Created /* 201 queue */)
            return;

        throw new InvalidOperationException(
            $"Jenkins trigger HTTP {(int)response.StatusCode}: {Truncate(errBody)}");
    }

    static string Truncate(string s, int max = 240)
    {
        s = s.Trim();
        if (s.Length <= max) return s;
        return s[..max] + "…";
    }
}
