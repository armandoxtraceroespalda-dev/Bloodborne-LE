using System.Runtime.InteropServices;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native.Platform;
/// <summary>Non-activating trophy presentation. Never records or changes an unlock.</summary>
internal sealed class TrophyBanner : IDisposable
{
    static readonly Dictionary<nint, TrophyBanner> Windows = new();
    static readonly W.WindowProc Procedure = Dispatch;
    static bool _registered;
    LayeredBitmap? _surface;
    NativeTrophySound? _sound;
    Action? _closedCallback;
    long _started;
    nint _window;
    bool _disposed;
    int _x, _y;
    public bool IsDisposed => _disposed;
    [DllImport("user32.dll")]
    static extern bool SystemParametersInfo(uint action, uint parameter, out W.Rect area, uint flags);

    public TrophyBanner(nint owner, string assets, LauncherSettings settings, Trophy? awarded = null,
        bool cheater = false, Action? closed = null)
    {
        try
        {
            uint dpi = W.GetDpiForWindow(owner);
            if (dpi == 0) dpi = 96;
            float scale = dpi / 96f;
            const float width = 420;
            var trophy = awarded ?? TrophyTracker.ById[14];
            string title = UiText.T(cheater ? "Cheater!" : trophy.Name);
            string description = cheater ? UiText.T("I can't git gud...") : "";
            // Use the same extracted menu font/fallback and renderer as the launcher.
            // Rasterize once, then release all Direct2D/DirectWrite/WIC resources.
            using (var canvas = new Canvas(assets))
            {
                float titleFont = cheater ? 18 : 20;
                float titleHeight = canvas.WrappedHeight(title, titleFont, width - 132);
                float descriptionHeight = cheater ? canvas.WrappedHeight(description, 20, width - 132) : 0;
                float textHeight = titleHeight + (cheater ? 8 + descriptionHeight : 0);
                float height = Math.Max(116, 42 + textHeight);
                float textTop = (height - textHeight) / 2;
                var scene = new List<Op>
                {
                    new(Ink.SoftSurface, new Rect(0, 0, width, height), new Color4(0, 0, 0, .85f), Image: "dialog-black")
                };
                string? icon = cheater ? null : TrophyIcons.For(settings, trophy.Id);
                var iconBox = new Rect(28, (height - 48) / 2, 48, 48);
                if (cheater)
                    scene.Add(new(Ink.Text, iconBox, new Color4(1, 1, 1, 1), "🍼", 34, Center: true, Emoji: true));
                else if (icon != null)
                    scene.Add(new(Ink.Image, iconBox, new Color4(1, 1, 1, 1), Image: icon));
                else
                    scene.Add(new(Ink.Text, iconBox, new Color4(212 / 255f, 196 / 255f, 157 / 255f, 1), "\uE735", 30, Center: true, Symbol: true));
                scene.Add(new(Ink.Text, new Rect(94, textTop, width - 132, titleHeight),
                    cheater ? new Color4(212 / 255f, 196 / 255f, 157 / 255f, 1) : new Color4(.89f, .89f, .87f, 1),
                    title, titleFont, Wrap: true));
                if (cheater)
                    scene.Add(new(Ink.Text, new Rect(94, textTop + titleHeight + 8, width - 132, descriptionHeight),
                        new Color4(.89f, .89f, .87f, 1), description, 20, Wrap: true));
                int pixelWidth = (int)Math.Ceiling(width * scale), pixelHeight = (int)Math.Ceiling(height * scale);
                var pixels = canvas.Rasterize(scene, pixelWidth, pixelHeight, dpi);
                _surface = new LayeredBitmap(pixels, pixelWidth, pixelHeight);
                if (!SystemParametersInfo(0x30, 0, out var area, 0)) throw new System.ComponentModel.Win32Exception();
                int gap = (int)(18 * scale);
                string position = settings.Str("trophy_position");
                _x = position.Contains("left", StringComparison.Ordinal) ? area.Left + gap : area.Right - pixelWidth - gap;
                _y = position.StartsWith("top", StringComparison.Ordinal) ? area.Top + gap : area.Bottom - pixelHeight - gap;
                if (!_registered)
                {
                    var cls = new W.WindowClass
                    {
                        Size = (uint)Marshal.SizeOf<W.WindowClass>(), Proc = Procedure,
                        Instance = W.GetModuleHandle(null), Name = "BloodborneNativeTrophySample"
                    };
                    if (W.RegisterClassEx(ref cls) == 0) throw new System.ComponentModel.Win32Exception();
                    _registered = true;
                }
                _window = W.CreateWindowEx(0x08080088, "BloodborneNativeTrophySample", "", 0x80000000,
                    _x, _y, pixelWidth, pixelHeight, 0, 0, W.GetModuleHandle(null), 0);
                if (_window == 0) throw new System.ComponentModel.Win32Exception();
                Windows.Add(_window, this);
                _surface.Present(_window, _x, _y, 0);
            }
            W.ShowWindow(_window, 4); // SW_SHOWNOACTIVATE
            _started = Environment.TickCount64;
            if (W.SetTimer(_window, 1, 33, 0) == 0) throw new System.ComponentModel.Win32Exception();
            if (settings.Bool("trophy_sound"))
                _sound = new NativeTrophySound(settings.Int("trophy_volume"), settings.Str("trophy_sound_file"));
            _closedCallback = closed;
        }
        catch { Dispose(); throw; }
    }

    static nint Dispatch(nint h, uint message, nint w, nint l)
    {
        if (Windows.TryGetValue(h, out var banner))
        {
            if (message == 0x14) return 1;
            if (message == 0x21) return 3; // MA_NOACTIVATE
            if (message == 0xF)
            {
                // Windows composites the retained per-pixel DIB, not an opaque HWND target.
                W.BeginPaint(h, out var paint);
                W.EndPaint(h, ref paint);
                return 0;
            }
            if (message == 0x113)
            {
                double elapsed = (Environment.TickCount64 - banner._started) / 1000d;
                if (elapsed >= 5) banner.Dispose();
                else
                {
                    byte alpha = (byte)(255 * Math.Clamp(Math.Min(elapsed / .2, (5 - elapsed) / .4), 0, 1));
                    try { banner._surface?.Present(h, banner._x, banner._y, alpha); }
                    catch (System.ComponentModel.Win32Exception) { banner.Dispose(); }
                }
                return 0;
            }
            if (message == 0x201) { banner.Dispose(); return 0; }
        }
        return W.DefWindowProc(h, message, w, l);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window != 0)
        {
            W.KillTimer(_window, 1);
            Windows.Remove(_window);
            W.DestroyWindow(_window);
            _window = 0;
        }
        try { _sound?.Dispose(); }
        finally
        {
            _sound = null;
            _surface?.Dispose();
            _surface = null;
            var completed = _closedCallback;
            _closedCallback = null;
            completed?.Invoke();
        }
    }
}
