using System.Reflection;

namespace Firestarter.Core.Updates;

public record UpdateManifest(string Version, string DownloadUrl, string? Sha256);

public record UpdateState(
    string CurrentVersion,
    /// <summary>Last manifest pulled successfully, or null if never polled / unreachable.</summary>
    UpdateManifest? Latest,
    /// <summary>True when <c>Latest</c> exists and its version is greater than <c>CurrentVersion</c>.</summary>
    bool UpdateAvailable,
    /// <summary>Most recent error from the manifest check, surfaced for diagnostics.</summary>
    string? LastError,
    DateTimeOffset? LastCheckedAt,
    /// <summary>True while a download/apply is in progress so the UI can disable the trigger.</summary>
    bool ApplyInProgress);

/// <summary>
/// In-memory snapshot of the update state. Singleton; used by the background poller (writer)
/// and the IPC handler / installer (readers).
/// </summary>
public sealed class UpdateStatusHub
{
    readonly Lock _gate = new();
    UpdateState _state;

    public UpdateStatusHub()
    {
        _state = new UpdateState(
            CurrentVersion: GetCurrentVersion(),
            Latest: null,
            UpdateAvailable: false,
            LastError: null,
            LastCheckedAt: null,
            ApplyInProgress: false);
    }

    public UpdateState Snapshot
    {
        get { lock (_gate) return _state; }
    }

    public void Update(Func<UpdateState, UpdateState> mutate)
    {
        lock (_gate)
        {
            _state = mutate(_state);
        }
    }

    static string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip "+gitsha" suffix from informational versions.
            var plus = info.IndexOf('+');
            if (plus > 0) info = info[..plus];
            return info;
        }
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static bool IsNewer(string latest, string current)
    {
        if (TryParse(latest, out var a) && TryParse(current, out var b))
            return Compare(a, b) > 0;
        // Fallback: ordinal compare (still better than nothing).
        return string.CompareOrdinal(latest, current) > 0;
    }

    static bool TryParse(string s, out int[] parts)
    {
        parts = [];
        var pieces = s.Split('.', '-');
        var nums = new List<int>(pieces.Length);
        foreach (var p in pieces)
        {
            if (int.TryParse(p, out var n)) nums.Add(n);
            else break;
        }
        if (nums.Count == 0) return false;
        parts = [.. nums];
        return true;
    }

    static int Compare(int[] a, int[] b)
    {
        var n = Math.Max(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av != bv) return av - bv;
        }
        return 0;
    }
}
