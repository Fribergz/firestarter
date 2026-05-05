using System.Net.Http.Headers;
using System.Text;

namespace Firestarter.Core.Jenkins;

/// <summary>Minimal Jenkins REST check using HTTP Basic (username + API token).</summary>
public static class JenkinsConnectionProbe
{
    /// <returns>User-visible error message, or null when the API responds successfully.</returns>
    public static async Task<string?> TestAsync(string baseUrl, string username, string apiToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return "Jenkins base URL is empty.";
        if (string.IsNullOrWhiteSpace(username)) return "Jenkins username is empty.";
        if (string.IsNullOrWhiteSpace(apiToken)) return "Jenkins API token is empty.";

        var root = baseUrl.TrimEnd('/');
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.GetAsync($"{root}/api/json?tree=nodeName", ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var hint = string.IsNullOrWhiteSpace(body) ? (response.ReasonPhrase ?? "") : body.Trim();
        if (hint.Length > 200) hint = hint[..200] + "…";
        return $"HTTP {(int)response.StatusCode}: {hint}";
    }
}
