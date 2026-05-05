using System.Runtime.InteropServices;

namespace Firestarter.App.Win32;

/// <summary>
/// Toggle whether the main window gets a standard taskbar button by flipping <c>WS_EX_APPWINDOW</c> /
/// <c>WS_EX_TOOLWINDOW</c>. Used so the app shows in the taskbar while open, and only the tray after hide-to-tray.
/// </summary>
static partial class WindowsTaskbarHiding
{
    const int GwlExStyle = -20;
    const nuint WsExAppWindow = 0x0004_0000;
    const nuint WsExToolWindow = 0x0000_0080;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    /// <summary>Best-effort: clears WS_EX_APPWINDOW and sets WS_EX_TOOLWINDOW (no taskbar button).</summary>
    public static void RemoveWindowFromTaskbar(nint nativeWindow)
    {
        if (nativeWindow == 0) return;
        nuint style = (nuint)(nint)GetWindowLongPtr(nativeWindow, GwlExStyle);
        style = (style & ~WsExAppWindow) | WsExToolWindow;
        SetWindowLongPtr(nativeWindow, GwlExStyle, (nint)style);
    }

    /// <summary>Best-effort: sets <c>WS_EX_APPWINDOW</c> and clears <c>WS_EX_TOOLWINDOW</c> (normal taskbar button).</summary>
    public static void RestoreWindowToTaskbar(nint nativeWindow)
    {
        if (nativeWindow == 0) return;
        nuint style = (nuint)(nint)GetWindowLongPtr(nativeWindow, GwlExStyle);
        style = (style & ~WsExToolWindow) | WsExAppWindow;
        SetWindowLongPtr(nativeWindow, GwlExStyle, (nint)style);
    }
}
