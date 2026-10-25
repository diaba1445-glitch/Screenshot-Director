using System.Runtime.InteropServices;

namespace ScreenshotDirector.Platform;

internal static class NativeMethods
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_SHOW_SETTINGS = 0x0400 + 101; // Custom application message

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClipboardFormatName(uint format, [Out] System.Text.StringBuilder lpszFormatName, int cchMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // Standard Clipboard Formats
    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_DIBV5 = 17;
}
