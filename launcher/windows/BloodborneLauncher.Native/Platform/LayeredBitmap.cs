using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BloodborneLauncher.Native.Platform;
/// <summary>One retained premultiplied BGRA DIB. Presenting opacity does not repaint or allocate.</summary>
internal sealed class LayeredBitmap : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixels, YPixels;
        public uint Colors, Important;
        public uint Color;
    }
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(nint window, nint screen, ref Point position, ref Size size, nint source, ref Point origin, uint key, ref Blend blend, uint flags);
    nint _dc, _bitmap, _previous;
    readonly int _width, _height;

    public LayeredBitmap(byte[] pixels, int width, int height)
    {
        if (width < 1 || height < 1 || pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid BGRA bitmap dimensions.");
        _width = width; _height = height;
        nint screen = GetDC(0);
        try
        {
            _dc = CreateCompatibleDC(screen);
            if (_dc == 0) throw new Win32Exception();
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            _bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
            if (_bitmap == 0 || bits == 0) throw new Win32Exception();
            _previous = SelectObject(_dc, _bitmap);
            if (_previous == 0 || _previous == -1) throw new Win32Exception();
            Marshal.Copy(pixels, 0, bits, pixels.Length);
        }
        catch { Dispose(); throw; }
        finally { if (screen != 0) ReleaseDC(0, screen); }
    }

    public void Present(nint window, int x, int y, byte opacity)
    {
        var position = new Point { X = x, Y = y };
        var size = new Size { Width = _width, Height = _height };
        var origin = new Point();
        var blend = new Blend { Alpha = opacity, Format = 1 }; // AC_SRC_ALPHA
        if (!UpdateLayeredWindow(window, 0, ref position, ref size, _dc, ref origin, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_dc != 0 && _previous != 0 && _previous != -1) SelectObject(_dc, _previous);
        if (_bitmap != 0) DeleteObject(_bitmap);
        if (_dc != 0) DeleteDC(_dc);
        _bitmap = _dc = _previous = 0;
    }
}
