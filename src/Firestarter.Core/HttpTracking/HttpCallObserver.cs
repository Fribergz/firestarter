using System.Diagnostics;
using System.Runtime.CompilerServices;
using Firestarter.Core.Data.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Firestarter.Core.HttpTracking;

/// <summary>
/// Subscribes to the <c>HttpHandlerDiagnosticListener</c> so every outbound <see cref="HttpClient"/>
/// request in the process is captured — including those issued by NGitLab, the Jenkins client, or any
/// other transitive caller — and forwarded to <see cref="HttpCallRecorder"/>.
/// </summary>
public sealed partial class HttpCallObserver(HttpCallRecorder recorder, ILogger<HttpCallObserver> logger) : IHostedService, IObserver<DiagnosticListener>, IDisposable
{
    readonly List<IDisposable> _innerSubs = [];
    IDisposable? _allListenersSub;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _allListenersSub = DiagnosticListener.AllListeners.Subscribe(this);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "HttpHandlerDiagnosticListener")
        {
            try
            {
                _innerSubs.Add(listener.Subscribe(new HttpListenerObserver(recorder, logger)));
            }
            catch (Exception ex)
            {
                LogSubscribeFailed(logger, ex);
            }
        }
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }

    public void Dispose()
    {
        foreach (var sub in _innerSubs)
        {
            try { sub.Dispose(); } catch { }
        }
        _innerSubs.Clear();
        _allListenersSub?.Dispose();
        _allListenersSub = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not subscribe to HttpHandlerDiagnosticListener")]
    static partial void LogSubscribeFailed(ILogger logger, Exception exception);
}

sealed partial class HttpListenerObserver(HttpCallRecorder recorder, ILogger logger) : IObserver<KeyValuePair<string, object?>>
{
    static readonly ConditionalWeakTable<HttpRequestMessage, RequestState> InFlight = [];

    public void OnNext(KeyValuePair<string, object?> kv)
    {
        try
        {
            switch (kv.Key)
            {
                case "System.Net.Http.HttpRequestOut.Start":
                    HandleStart(kv.Value);
                    break;
                case "System.Net.Http.HttpRequestOut.Stop":
                    HandleStop(kv.Value, exception: null);
                    break;
                case "System.Net.Http.Exception":
                    HandleException(kv.Value);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogEventHandlingError(logger, kv.Key, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Error handling HTTP diagnostic event '{Key}'")]
    static partial void LogEventHandlingError(ILogger logger, string key, Exception exception);

    static void HandleStart(object? payload)
    {
        if (payload is null) return;
        var request = ExtractRequest(payload);
        if (request is null) return;
        InFlight.AddOrUpdate(request, new RequestState(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp()));
    }

    void HandleStop(object? payload, Exception? exception)
    {
        if (payload is null) return;
        var request = ExtractRequest(payload);
        if (request is null) return;
        if (!InFlight.TryGetValue(request, out var state)) return;
        InFlight.Remove(request);

        var response = ExtractResponse(payload);
        var elapsed = (int)Stopwatch.GetElapsedTime(state.StartTicks).TotalMilliseconds;

        var uri = request.RequestUri;
        var host = uri?.Host ?? string.Empty;
        var path = uri is null
            ? string.Empty
            : uri.AbsolutePath + (string.IsNullOrEmpty(uri.Query) ? string.Empty : uri.Query);

        long reqBytes = 0;
        if (request.Content?.Headers.ContentLength is long rl && rl > 0) reqBytes = rl;

        long resBytes = 0;
        if (response?.Content?.Headers.ContentLength is long sl && sl > 0) resBytes = sl;

        var entry = new ApiCallLog
        {
            Timestamp = state.StartedAt,
            Method = request.Method.Method,
            Host = Truncate(host, 256),
            Path = Truncate(path, 2048),
            StatusCode = response is null ? 0 : (int)response.StatusCode,
            DurationMs = elapsed,
            RequestBytes = reqBytes,
            ResponseBytes = resBytes,
            Source = ClassifySource(host),
            ErrorMessage = exception?.Message,
        };

        recorder.Enqueue(entry);
    }

    void HandleException(object? payload)
    {
        if (payload is null) return;
        var request = ExtractRequest(payload);
        var ex = ExtractException(payload);
        if (request is null) return;
        if (!InFlight.TryGetValue(request, out var state)) return;
        InFlight.Remove(request);

        var uri = request.RequestUri;
        var host = uri?.Host ?? string.Empty;
        var path = uri is null
            ? string.Empty
            : uri.AbsolutePath + (string.IsNullOrEmpty(uri.Query) ? string.Empty : uri.Query);
        var elapsed = (int)Stopwatch.GetElapsedTime(state.StartTicks).TotalMilliseconds;

        recorder.Enqueue(new ApiCallLog
        {
            Timestamp = state.StartedAt,
            Method = request.Method.Method,
            Host = Truncate(host, 256),
            Path = Truncate(path, 2048),
            StatusCode = 0,
            DurationMs = elapsed,
            RequestBytes = 0,
            ResponseBytes = 0,
            Source = ClassifySource(host),
            ErrorMessage = Truncate(ex?.Message ?? "request failed", 1024),
        });
    }

    static HttpRequestMessage? ExtractRequest(object payload)
    {
        var p = payload.GetType().GetProperty("Request");
        return p?.GetValue(payload) as HttpRequestMessage;
    }

    static HttpResponseMessage? ExtractResponse(object payload)
    {
        var p = payload.GetType().GetProperty("Response");
        return p?.GetValue(payload) as HttpResponseMessage;
    }

    static Exception? ExtractException(object payload)
    {
        var p = payload.GetType().GetProperty("Exception");
        return p?.GetValue(payload) as Exception;
    }

    static string ClassifySource(string host) => HttpSourceClassifier.Classify(host);

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s[..max];
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }

    sealed record RequestState(DateTimeOffset StartedAt, long StartTicks);
}
