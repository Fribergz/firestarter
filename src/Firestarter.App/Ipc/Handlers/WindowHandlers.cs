using Firestarter.App.Win32;
using Photino.NET;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Firestarter.App.Ipc.Handlers;

static partial class NativeWindow
{
    public const uint WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 0x2;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static int HitTestFor(string edge) => edge switch
    {
        "top" => HTTOP,
        "right" => HTRIGHT,
        "bottom" => HTBOTTOM,
        "left" => HTLEFT,
        "top-left" => HTTOPLEFT,
        "top-right" => HTTOPRIGHT,
        "bottom-left" => HTBOTTOMLEFT,
        "bottom-right" => HTBOTTOMRIGHT,
        _ => HTCAPTION,
    };

    /// <summary>Must not run synchronously from WebView2’s WebMessageReceived callback — defer first (see window drag/resize handlers).</summary>
    public static void BeginMoveOrResize(IntPtr hWnd, int hitTest)
    {
        if (hWnd == IntPtr.Zero) return;
        _ = ReleaseCapture();
        _ = SendMessage(hWnd, WM_NCLBUTTONDOWN, new IntPtr(hitTest), IntPtr.Zero);
    }
}

public class WindowMinimizeHandler(WindowAccessor accessor) : IIpcHandler
{
    readonly WindowAccessor _accessor = accessor;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        _ = ct;
        var w = _accessor.Window;
        w.Invoke(() => w.Minimized = true);
        return Task.FromResult<object?>(new { ok = true });
    }
}

public class WindowToggleMaximizeHandler(WindowAccessor accessor) : IIpcHandler
{
    readonly WindowAccessor _accessor = accessor;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        _ = ct;
        var w = _accessor.Window;
        var maximized = false;
        w.Invoke(() =>
        {
            w.Maximized = !w.Maximized;
            maximized = w.Maximized;
        });
        return Task.FromResult<object?>(new { maximized });
    }
}

public class WindowCloseHandler(WindowAccessor accessor) : IIpcHandler
{
    readonly WindowAccessor _accessor = accessor;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        _ = ct;
        var w = _accessor.Window;
        w.Invoke(w.Close);
        return Task.FromResult<object?>(new { ok = true });
    }
}

/// <summary>Chromeless close (X): hide the window; app stays running in the system tray (Windows: SW_HIDE, no taskbar button).</summary>
public class WindowHideToTrayHandler(WindowAccessor accessor) : IIpcHandler
{
    readonly WindowAccessor _accessor = accessor;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        _ = ct;
        var w = _accessor.Window;
        w.Invoke(() =>
        {
            if (PhotinoWindow.IsWindowsPlatform)
            {
                var h = (nint)w.WindowHandle;
                _ = NativeWindowVisibility.Hide(h);
                WindowsTaskbarHiding.RemoveWindowFromTaskbar(h);
            }
            else
            {
                w.Minimized = true;
            }
        });
        return Task.FromResult<object?>(new { ok = true });
    }
}

public class WindowStateHandler(WindowAccessor accessor) : IIpcHandler
{
    readonly WindowAccessor _accessor = accessor;

    public Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        _ = ct;
        var w = _accessor.Window;
        return Task.FromResult<object?>(new { maximized = w.Maximized, minimized = w.Minimized });
    }
}

public class WindowStartDragHandler(WindowAccessor accessor) : IIpcHandler
{
    readonly WindowAccessor _accessor = accessor;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        _ = payload;
        var w = _accessor.Window;
        // WebMessageReceived runs inside WebView2’s native callback stack. Entering the modal
        // move/resize loop via SendMessage from that stack can crash the process (0xC000041D).
        await Task.Yield();
        if (ct.IsCancellationRequested) return new { ok = true };
        w.Invoke(() => NativeWindow.BeginMoveOrResize(w.WindowHandle, NativeWindow.HTCAPTION));
        return new { ok = true };
    }
}

public class WindowStartResizeHandler(WindowAccessor accessor) : IIpcHandler
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    readonly WindowAccessor _accessor = accessor;

    public async Task<object?> HandleAsync(JsonElement? payload, CancellationToken ct)
    {
        var dto = payload?.Deserialize<ResizePayload>(Options) ?? new ResizePayload();
        var hit = NativeWindow.HitTestFor(dto.Edge ?? "");
        var w = _accessor.Window;
        await Task.Yield();
        if (ct.IsCancellationRequested) return new { ok = true };
        w.Invoke(() => NativeWindow.BeginMoveOrResize(w.WindowHandle, hit));
        return new { ok = true };
    }

    class ResizePayload { public string? Edge { get; set; } }
}
