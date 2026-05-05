using System.Runtime.InteropServices;

namespace Firestarter.App.Win32;

static partial class NativeWindowVisibility
{
    public const int SwHide = 0;
    public const int SwShow = 5;
    public const int SwRestore = 9;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    public static bool Hide(nint hWnd) => hWnd != 0 && ShowWindow(hWnd, SwHide);

    public static bool Show(nint hWnd) => hWnd != 0 && ShowWindow(hWnd, SwShow);
}
