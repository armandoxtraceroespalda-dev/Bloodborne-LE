using System.Runtime.InteropServices;

namespace BloodborneLauncher.Native.Platform;
internal static class Win32
{
    internal delegate nint WindowProc(nint h, uint msg, nint w, nint l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size, Style;
        public WindowProc Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? Menu, Name;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public Point Pt;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Paint
    {
        public nint Dc;
        public int Erase;
        public Rect Area;
        public int Restore, Update;
        public long R0, R1, R2, R3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMax
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Memory
    {
        public uint Size, PageFaults;
        public nuint PeakWorking, Working, QuotaPeakPaged, QuotaPaged, QuotaPeakNonPaged, QuotaNonPaged, Pagefile, PeakPagefile, Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassEx(ref WindowClass c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint args);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    internal static extern nint LoadCursor(nint instance, nint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProc(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Msg m, nint h, uint min, uint max);
    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg m);
    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Msg m);
    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")]
    internal static extern bool PostMessage(nint window, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")]
    internal static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(nint h);
    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint h);
    [DllImport("user32.dll")]
    internal static extern bool IsWindowEnabled(nint h);
    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint h, out Paint p);
    [DllImport("user32.dll")]
    internal static extern bool EndPaint(nint h, ref Paint p);
    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint h, out Rect r);
    [DllImport("user32.dll")]
    internal static extern bool InvalidateRect(nint h, nint r, bool erase);
    [DllImport("user32.dll", EntryPoint = "InvalidateRect")]
    internal static extern bool InvalidateArea(nint h, ref Rect r, bool erase);
    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint h, nuint id, uint ms, nint callback);
    [DllImport("user32.dll")]
    internal static extern bool KillTimer(nint h, nuint id);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32.dll")]
    internal static extern bool ScreenToClient(nint h, ref Point p);
    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint window);
    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint h);
    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(nint h, string text, string title, uint flags);
    [DllImport("kernel32.dll")]
    internal static extern nint GetCurrentProcess();
    [DllImport("psapi.dll")]
    internal static extern bool GetProcessMemoryInfo(nint p, ref Memory m, uint size);
    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint h, nint insert, int x, int y, int w, int height, uint flags);
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint h, uint a, ref int value, int size);
}
