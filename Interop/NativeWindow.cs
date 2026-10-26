using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FloatTodo.Interop;

// 所有屏幕边界与窗口移动统一采用物理像素，兼容负坐标显示器布局。
internal static class NativeWindow
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PixelPoint
    {
        public int X;
        public int Y;
        public PixelPoint(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PixelRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public int CenterX => Left + Width / 2;
        public int CenterY => Top + Height / 2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Monitor;
        public PixelRect Work;
        public uint Flags;
    }

    private const int MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out PixelPoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr handle, out PixelRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(PixelPoint point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public static PixelPoint Cursor()
    {
        if (!GetCursorPos(out var point))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return point;
    }

    public static PixelRect Rect(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return rect;
    }

    public static void Move(Window window, int x, int y)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public static PixelRect WorkAreaNear(int physicalX, int physicalY)
    {
        var monitor = MonitorFromPoint(new PixelPoint(physicalX, physicalY), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return info.Work;
    }

    public static bool Contains(PixelRect rect, PixelPoint p) =>
        p.X >= rect.Left && p.X < rect.Right && p.Y >= rect.Top && p.Y < rect.Bottom;
}
