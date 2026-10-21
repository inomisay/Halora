using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Halora.App;

internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int VK_SHIFT = 0x10;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

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
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static bool IsShiftDown() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    /// <summary>Tool window (no taskbar or Alt+Tab entry), optionally never takes focus.</summary>
    public static void MakeToolWindow(Window window, bool noActivate)
    {
        var hwnd = Handle(window);
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        if (noActivate)
        {
            style |= WS_EX_NOACTIVATE;
        }

        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>
    /// Centers the window on a point given in physical screen pixels. Working in pixels
    /// avoids WPF's per-monitor DPI conversions when screens have different scaling.
    /// </summary>
    public static void CenterOn(Window window, POINT screenPoint, bool keepOnScreen = false)
    {
        var hwnd = Handle(window);
        if (!GetWindowRect(hwnd, out var r))
        {
            return;
        }

        var w = r.Right - r.Left;
        var h = r.Bottom - r.Top;
        var x = screenPoint.X - (w / 2);
        var y = screenPoint.Y - (h / 2);

        if (keepOnScreen)
        {
            var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(screenPoint.X, screenPoint.Y)).WorkingArea;
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));
        }

        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Puts the window in the bottom-right corner of the screen under the cursor.</summary>
    public static void PlaceBottomRight(Window window, int marginPx, int stackIndex = 0)
    {
        var hwnd = Handle(window);
        if (!GetWindowRect(hwnd, out var r) || !GetCursorPos(out var cursor))
        {
            return;
        }

        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        var w = r.Right - r.Left;
        var h = r.Bottom - r.Top;
        var y = area.Bottom - h - marginPx - (stackIndex * (h + (marginPx / 2)));
        SetWindowPos(hwnd, HWND_TOPMOST, area.Right - w - marginPx, Math.Max(area.Top, y), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool Contains(Window window, POINT screenPoint)
    {
        if (!GetWindowRect(Handle(window), out var r))
        {
            return false;
        }

        return screenPoint.X >= r.Left && screenPoint.X < r.Right && screenPoint.Y >= r.Top && screenPoint.Y < r.Bottom;
    }
}
