namespace Firestarter.Core.Jenkins;

/// <summary>
/// Derives a stored Jenkins job path from a GitLab project hook URL when it targets the configured Jenkins instance.
/// </summary>
public static class JenkinsGitLabWebhookResolver
{
    static readonly HashSet<string> TrailingPathNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "build",
        "buildWithParameters",
        "polling",
    };

    /// <summary>
    /// Returns a slash-separated job path (same shape as <see cref="JenkinsRestClient.NormalizeJobPath"/> output)
    /// or <c>null</c> when the hook does not point at <paramref name="jenkinsBaseUrl"/> or no job path can be inferred.
    /// </summary>
    public static string? TryDeriveJobPathFromHookUrl(string hookUrl, string jenkinsBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(hookUrl) || string.IsNullOrWhiteSpace(jenkinsBaseUrl))
            return null;

        if (!Uri.TryCreate(hookUrl.Trim(), UriKind.Absolute, out var hookUri))
            return null;
        if (hookUri.Scheme != Uri.UriSchemeHttp && hookUri.Scheme != Uri.UriSchemeHttps)
            return null;

        var baseTrim = jenkinsBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseTrim, UriKind.Absolute, out var baseUri))
            return null;

        if (!string.Equals(hookUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            return null;
        if (hookUri.Port != baseUri.Port)
            return null;

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var hookPath = hookUri.AbsolutePath;

        string relative;
        if (string.IsNullOrEmpty(basePath) || basePath == "/")
        {
            relative = hookPath.TrimStart('/');
        }
        else
        {
            if (!hookPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return null;
            relative = hookPath.AsSpan(basePath.Length).TrimStart('/').ToString();
        }

        if (string.IsNullOrEmpty(relative))
            return null;

        var decodedRelative = DecodePathSegments(relative);
        if (string.IsNullOrEmpty(decodedRelative))
            return null;

        var segments = decodedRelative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return null;

        while (segments.Length > 0 && TrailingPathNoise.Contains(segments[^1]))
            segments = segments[..^1];
        if (segments.Length == 0)
            return null;

        if (segments[0].Equals("job", StringComparison.OrdinalIgnoreCase))
        {
            var fromJob = TryParseJobFolderPath(segments);
            if (!string.IsNullOrEmpty(fromJob))
                return JenkinsRestClient.NormalizeJobPath(fromJob);
        }

        if (segments[0].Equals("project", StringComparison.OrdinalIgnoreCase))
            return NormalizeOrNull(JenkinsRestClient.NormalizeJobPath(string.Join("/", segments)));

        return null;
    }

    static string DecodePathSegments(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Uri.UnescapeDataString(parts[i]);
        return string.Join("/", parts);
    }

    static string? TryParseJobFolderPath(string[] segments)
    {
        var names = new List<string>();
        var i = 0;
        while (i < segments.Length)
        {
            if (!segments[i].Equals("job", StringComparison.OrdinalIgnoreCase))
                return null;
            if (i + 1 >= segments.Length)
                return null;
            names.Add(segments[i + 1]);
            i += 2;
        }

        return names.Count == 0 ? null : string.Join("/", names);
    }

    static string? NormalizeOrNull(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        var t = normalized.Trim();
        return t.Length == 0 ? null : t;
    }
}
