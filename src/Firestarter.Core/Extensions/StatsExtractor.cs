using System.Text.Json;

namespace Firestarter.Core.Extensions;

public static class StatsExtractor
{
    public static string? Extract(string stdout)
    {
        if (string.IsNullOrEmpty(stdout)) return null;

        var fromFence = ExtractFencedJson(stdout);
        if (fromFence is not null && IsValidJson(fromFence)) return fromFence;

        var fromTrailing = ExtractTrailingJson(stdout);
        if (fromTrailing is not null && IsValidJson(fromTrailing)) return fromTrailing;

        return null;
    }

    static string? ExtractFencedJson(string stdout)
    {
        const string open = "```json";
        var lastIdx = stdout.LastIndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (lastIdx < 0) return null;

        var after = lastIdx + open.Length;
        var endIdx = stdout.IndexOf("```", after, StringComparison.Ordinal);
        if (endIdx < 0) return null;

        return stdout[after..endIdx].Trim();
    }

    static string? ExtractTrailingJson(string stdout)
    {
        var trimmed = stdout.TrimEnd();
        if (trimmed.Length == 0 || trimmed[^1] != '}') return null;

        int depth = 0;
        int start = -1;
        for (int i = trimmed.Length - 1; i >= 0; i--)
        {
            var c = trimmed[i];
            if (c == '}') depth++;
            else if (c == '{')
            {
                depth--;
                if (depth == 0) { start = i; break; }
            }
        }

        if (start < 0) return null;
        return trimmed[start..];
    }

    static bool IsValidJson(string candidate)
    {
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
