namespace Firestarter.Core.HttpTracking;

/// <summary>
/// Coarse origin label for an outbound HTTP host. Centralised so the live recorder and the
/// stats query path stay in sync; existing rows are reclassified on read.
/// </summary>
public static class HttpSourceClassifier
{
    public static string Classify(string? host)
    {
        if (string.IsNullOrEmpty(host)) return "other";
        var h = host.ToLowerInvariant();
        if (h.Contains("gitlab")) return "gitlab";
        if (h.Contains("jenkins")) return "jenkins";
        if (h.Contains("git")) return "git";
        return "other";
    }
}
