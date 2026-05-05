using Firestarter.App.Win32;
using Photino.NET;
using System.Runtime.InteropServices;
using Wm = System.Windows.Forms;

namespace Firestarter.App;

/// <summary>
/// Notification area icon and menu (Open / Exit) using the same .ico as the native title bar and Photino
/// <see cref="PhotinoWindow.SetIconFile" />.
/// </summary>
sealed partial class FirestarterSystemTray(PhotinoWindow window) : IDisposable
{
    readonly PhotinoWindow _window = window;
    Wm.NotifyIcon? _tray;
    bool _disposed;

    /// <param name="hideFromTaskbar">When true, skip the normal taskbar button (legacy; default is show in taskbar).</param>
    public void OnWindowCreated(Icon icon, bool hideFromTaskbar = false)
    {
        if (!PhotinoWindow.IsWindowsPlatform) return;

        if (hideFromTaskbar)
        {
            _window.Invoke(() => WindowsTaskbarHiding.RemoveWindowFromTaskbar((nint)_window.WindowHandle));
        }

        Wm.ContextMenuStrip menu = new();
        _ = menu.Items.Add("Open Firestarter", null, (_, _) => ShowAndActivate());
        _ = menu.Items.Add("Exit", null, (_, _) => _window.Close());

        _tray = new Wm.NotifyIcon
        {
            Text = "Firestarter",
            Icon = icon,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseDoubleClick += (_, _) => ShowAndActivate();
    }

    void ShowAndActivate()
    {
        _window.Invoke(() =>
        {
            if (PhotinoWindow.IsWindowsPlatform)
            {
                WindowsTaskbarHiding.RestoreWindowToTaskbar((nint)_window.WindowHandle);
                _ = NativeWindowVisibility.Show((nint)_window.WindowHandle);
            }
            _window.Minimized = false;
            _window.SetTopMost(true);
            _ = NativeMethods.SetForegroundWindow((nint)_window.WindowHandle);
            _window.SetTopMost(false);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_tray is { } t)
        {
            t.Visible = false;
            t.Dispose();
        }
    }

    static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(nint hWnd);
    }
}
