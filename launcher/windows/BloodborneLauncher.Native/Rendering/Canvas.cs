using System.Drawing;
using BloodborneLauncher.Native.Interaction;
using BloodborneLauncher.Core;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using Rect = Vortice.Mathematics.Rect;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;
using static Vortice.Direct2D1.D2D1;
using static Vortice.DirectWrite.DWrite;

namespace BloodborneLauncher.Native.Rendering;
public enum Ink
{
    Dim,
    Fill,
    Round,
    Shadow,
    Text,
    Line,
    Image,
    Glow,
    RoundImage,
    ScrollThumb,
    OutputThumb,
    ControlSurface,
    SoftSurface,
    Outline
}

public sealed record Op(Ink Kind, Rect Bounds, Color4 Color, string Text = "", float Size = 18, string Image = "", Rect? Source = null, bool Flip = false, bool Bold = false, float Radius = 0, bool Center = false, bool Pulse = false, bool Detail = false, bool Fixed = false, Rect? Clip = null, bool Wrap = false, int Trigger = -1, bool Selected = false, bool FocusOnly = false, bool Scrollable = false, bool Surface = false, bool FlipY = false, bool Symbol = false, bool Mono = false, int Weight = 0, Rect? ContentClip = null, bool Dialog = false, bool Emoji = false)
{
    public bool VisibleFor(int hover, int focus) => Trigger < 0 || Selected || Trigger == focus || (!FocusOnly && Trigger == hover);
}

public readonly record struct TextKey(string Text, float Size, float Width, float Height, bool Bold, bool Center, bool Detail, bool Wrap, bool Symbol = false, bool Mono = false, int Weight = 0, bool Emoji = false)
{
    public static TextKey From(Op op) => new(op.Text, op.Size, op.Bounds.Width, op.Bounds.Height, op.Bold, op.Center, op.Detail, op.Wrap, op.Symbol, op.Mono, op.Weight, op.Emoji);
}

public sealed class Canvas : IDisposable
{
    readonly ID2D1Factory _factory = D2D1CreateFactory<ID2D1Factory>();
    readonly IDWriteFactory3 _write = DWriteCreateFactory<IDWriteFactory3>();
    readonly IWICImagingFactory _wic = new();
    readonly Dictionary<string, ID2D1Bitmap> _images = new();
    readonly HashSet<string> _missingImages = new(StringComparer.Ordinal);
    readonly Dictionary<(float, bool, bool, bool, bool, bool, bool, int, bool), IDWriteTextFormat> _formats = new();
    readonly SceneResourceCache<TextKey, IDWriteTextLayout> _layouts = new();
    readonly Func<TextKey, IDWriteTextLayout> _createLayout;
    public int TextLayoutCount => _layouts.Count;
    public long LayoutsCreated => _layouts.Created;
    public long LayoutsReleased => _layouts.Released;
    public long Frames { get; private set; }
    public long ScenePrepares { get; private set; }

    ID2D1RenderTarget? _target;
    ID2D1SolidColorBrush? _brush;
    ID2D1BitmapBrush? _panel;
    ID2D1RadialGradientBrush? _shadow;
    ID2D1LinearGradientBrush? _controlSurface;
    IDWriteFontCollection? _fonts;
    string _family = "Georgia";
    readonly string _assets;
    public bool HasTarget => _target != null;
    public int ImageCount => _images.Count;
    public string Backend => _target == null ? "not created" : "Direct2D HWND / DirectWrite / Win32";

    public Canvas(string assets)
    {
        _createLayout = CreateLayout;
        _assets = assets;
        LoadFonts();
    }

    void LoadFonts()
    {
        var file = Path.Combine(_assets, "BloodborneMenu.ttf");
        if (!File.Exists(file))
            return;
        using var face = _write.CreateFontFaceReference(file, null, 0, FontSimulations.None);
        using var builder = _write.CreateFontSetBuilder();
        builder.AddFontFaceReference(face);
        using var set = builder.CreateFontSet();
        _fonts = _write.CreateFontCollectionFromFontSet(set);
        using var family = _fonts.GetFontFamily(0);
        using var names = family.FamilyNames;
        _family = names.GetString(0);
    }

    public void Resize(nint window, int width, int height, float dpi)
    {
        if (width < 1 || height < 1)
            return;
        if (_target == null)
        {
            _target = _factory.CreateHwndRenderTarget(new RenderTargetProperties(), new HwndRenderTargetProperties { Hwnd = window, PixelSize = new SizeI(width, height), PresentOptions = PresentOptions.RetainContents });
            _brush = _target.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        }
        else
            ((ID2D1HwndRenderTarget)_target).Resize(new SizeI(width, height));
        _target.Dpi = new Vortice.Mathematics.Size(dpi, dpi);
        _layouts.Clear();
        foreach (var f in _formats.Values)
            f.Dispose();
        _formats.Clear();
    }

    // Rasterize once for per-pixel layered windows; normal HWND rendering is unchanged.
    public byte[] Rasterize(IReadOnlyList<Op> ops, int width, int height, float dpi)
    {
        if (_target != null) throw new InvalidOperationException("Rasterize requires a fresh Canvas.");
        if (width < 1 || height < 1 || dpi <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        using var bitmap = _wic.CreateBitmap((uint)width, (uint)height, PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var properties = new RenderTargetProperties
        {
            Type = RenderTargetType.Software,
            PixelFormat = new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)
        };
        _target = _factory.CreateWicBitmapRenderTarget(bitmap, properties);
        _target.Dpi = new Vortice.Mathematics.Size(dpi, dpi);
        _target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        _brush = _target.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        PrepareScene(ops);
        Paint(ops, new Rect(0, 0, width * 96 / dpi, height * 96 / dpi), 1, 0, -1, -1, transparent: true);
        if (_target == null) throw new InvalidOperationException("Could not rasterize the notification.");
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels((uint)(width * 4), pixels);
        return pixels;
    }

    unsafe ID2D1Bitmap? Bitmap(string name)
    {
        if (_missingImages.Contains(name)) return null;
        if (_images.TryGetValue(name, out var result))
            return result;
        if (name is "soft-surface" or "soft-black" or "dialog-black" && _target != null)
        {
            const int size = SoftSurfacePixels.Size;
            bool dialog = name == "dialog-black";
            bool black = name is "soft-black" or "dialog-black";
            var bytes = SoftSurfacePixels.Create(black, dialog);
            fixed (byte* data = bytes)
            {
                var bitmap = _target.CreateBitmap(new SizeI(size, size), (nint)data, size * 4, new BitmapProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
                _images[name] = bitmap;
                return bitmap;
            }
        }

        bool gray = name.EndsWith("|gray", StringComparison.Ordinal);
        string fileName = gray ? name[..^5] : name;
        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(_assets, (fileName == "menu-glow-half-joined" ? "menu-glow-half" : fileName) + ".png");
        if (_target == null) return null;
        if (!File.Exists(path))
        {
            _missingImages.Add(name);
            return null;
        }
        using var decoder = _wic.CreateDecoderFromFileName(path);
        using var frame = decoder.GetFrame(0);
        using var converter = _wic.CreateFormatConverter();
        converter.Initialize(frame, PixelFormat.Format32bppPBGRA);
        if (gray)
        {
            int width = converter.Size.Width, height = converter.Size.Height;
            var pixels = new byte[checked(width * height * 4)];
            converter.CopyPixels((uint)(width * 4), pixels);
            ImagePixels.Grayscale(pixels);
            fixed (byte* data = pixels)
            {
                var prepared = _target.CreateBitmap(new SizeI(width, height), (nint)data, (uint)(width * 4),
                    new BitmapProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
                _images[name] = prepared;
                return prepared;
            }
        }
        if (name == "menu-glow-half-joined")
        {
            int w = converter.Size.Width, h = converter.Size.Height;
            var source = new byte[w * h * 4];
            converter.CopyPixels((uint)(w * 4), source);
            var joined = MenuPixels.PrepareHighlight(source, w, h, out int joinedWidth);
            fixed (byte* data = joined)
            {
                var prepared = _target.CreateBitmap(new SizeI(joinedWidth, h), (nint)data, (uint)(joinedWidth * 4), new BitmapProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
                _images[name] = prepared;
                return prepared;
            }
        }

        var image = _target.CreateBitmapFromWicBitmap(converter, new BitmapProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
        _images[name] = image;
        return image;
    }

    IDWriteTextFormat Format(TextKey op)
    {
        var key = (op.Size, op.Bold, op.Center, op.Detail, op.Wrap, op.Symbol, op.Mono, op.Weight, op.Emoji);
        if (_formats.TryGetValue(key, out var format))
            return format;
        format = _write.CreateTextFormat(op.Emoji ? "Segoe UI Emoji" : op.Symbol ? "Segoe MDL2 Assets" : op.Mono ? "Consolas" : op.Detail ? "Segoe UI" : _family, op.Emoji || op.Symbol || op.Mono || op.Detail ? null : _fonts, op.Weight > 0 ? (FontWeight)op.Weight : op.Bold ? FontWeight.Bold : FontWeight.Normal, FontStyle.Normal, FontStretch.Normal, op.Size, "en-us");
        format.TextAlignment = op.Center ? TextAlignment.Center : TextAlignment.Leading;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        format.WordWrapping = op.Wrap ? WordWrapping.Wrap : WordWrapping.NoWrap;
        _formats[key] = format;
        return format;
    }

    readonly record struct MetricKey(string Text, float Size, float Width, bool Detail, bool Mono, bool Wrap);
    readonly Dictionary<MetricKey, Vector2> _measurements = new();
    readonly HashSet<MetricKey> _measured = new();
    public int MeasurementCount => _measurements.Count;

    public void BeginPartialLayout() => _measured.Clear();
    public void BeginLayout()
    {
        _measured.Clear();
        // Retry optional assets when a scene is rebuilt, not on every glow frame.
        _missingImages.Clear();
    }
    Vector2 MeasureCore(MetricKey key)
    {
        _measured.Add(key);
        if (_measurements.TryGetValue(key, out var metrics))
            return metrics;
        using var layout = CreateLayout(new TextKey(key.Text, key.Size, key.Width, 10000, false, false, key.Detail, key.Wrap, Mono: key.Mono));
        metrics = new Vector2(layout.Metrics.WidthIncludingTrailingWhitespace, layout.Metrics.Height);
        _measurements[key] = metrics;
        return metrics;
    }

    public Vector2 Measure(string text, float size, bool detail = false, bool mono = false) => MeasureCore(new(text, size, 10000, detail, mono, false));
    public float WrappedHeight(string text, float size, float width, bool detail = false) => MeasureCore(new(text, size, Math.Max(1, width), detail, false, true)).Y;
    IDWriteTextLayout CreateLayout(TextKey key) => _write.CreateTextLayout(key.Text, Format(key), key.Width, key.Height);
    public int TextFormatCount => _formats.Count;
    public long ImageBytes => _images.Values.Sum(image => (long)image.PixelSize.Width * image.PixelSize.Height * 4);

    // Reuse scratch storage during wheel/tooltip updates instead of allocating
    // fresh sets and arrays for every viewport change.
    readonly HashSet<(float, bool, bool, bool, bool, bool, bool, int, bool)> _sceneFormats = new();
    readonly HashSet<string> _scenePictures = new(StringComparer.Ordinal);
    readonly List<MetricKey> _staleMeasurements = new();
    readonly List<(float, bool, bool, bool, bool, bool, bool, int, bool)> _staleFormats = new();
    readonly List<string> _staleImages = new();

    public void ReleaseBuildMetrics()
    {
        _measurements.Clear();
        _measurements.TrimExcess();
        _measured.Clear();
        _measured.TrimExcess();
        _staleMeasurements.Clear();
        _staleMeasurements.TrimExcess();
    }

    public void PrepareScene(IReadOnlyList<Op> ops, float scroll = 0)
    {
        _layouts.BeginScene();
        var formats = _sceneFormats;
        formats.Clear();
        foreach (var op in ops)
        {
            if (!ViewportResources.Keep(op, scroll))
                continue;
            var key = TextKey.From(op);
            _layouts.Use(key, _createLayout);
            formats.Add((key.Size, key.Bold, key.Center, key.Detail, key.Wrap, key.Symbol, key.Mono, key.Weight, key.Emoji));
        }

        _layouts.EndScene();
        _staleMeasurements.Clear();
        foreach (var key in _measurements.Keys)
            if (!_measured.Contains(key)) _staleMeasurements.Add(key);
        foreach (var key in _staleMeasurements) _measurements.Remove(key);
        _staleMeasurements.Clear();
        _staleFormats.Clear();
        foreach (var key in _formats.Keys)
            if (!formats.Contains(key)) _staleFormats.Add(key);
        foreach (var key in _staleFormats)
        {
            _formats[key].Dispose();
            _formats.Remove(key);
        }

        _staleFormats.Clear();
        var pictures = _scenePictures;
        pictures.Clear();
        bool panelVisible = false;
        foreach (var op in ops)
        {
            if (!ViewportResources.Visible(op, scroll)) continue;
            panelVisible |= op.Kind == Ink.RoundImage;
            if (op.Image.Length > 0)
                pictures.Add(op.Image);
            if (op.Kind == Ink.Glow)
                pictures.Add("menu-glow-half-joined");
        }

        if (!panelVisible)
        {
            _panel?.Dispose();
            _panel = null;
        }

        _staleImages.Clear();
        foreach (var name in _images.Keys)
            if (!pictures.Contains(name)) _staleImages.Add(name);
        foreach (var name in _staleImages)
        {
            _images[name].Dispose();
            _images.Remove(name);
        }

        _staleImages.Clear();
        ScenePrepares++;
    }

    public void UpdateViewport(IReadOnlyList<Op> ops, float scroll) => PrepareScene(ops, scroll);
    public static bool Intersects(Rect a, Rect b) => a.Right > b.Left && a.Left < b.Right && a.Bottom > b.Top && a.Top < b.Bottom;
    public void Paint(IReadOnlyList<Op> ops, Rect dirty, float fade, float time, int hover, int focus, float scroll = 0, float scrollMax = 0, float surfaceFade = 1, float dim = 1, float outputOffset = 0, float outputMaximum = 0, float dialogFade = 1, bool transparent = false, Rect? navigationFocus = null, float navigationOpacity = 1)
    {
        if (_target == null || _brush == null)
            return;
        _target.BeginDraw();
        _target.PushAxisAlignedClip(new Vortice.RawRectF(dirty.Left, dirty.Top, dirty.Right, dirty.Bottom), AntialiasMode.PerPrimitive);
        _target.Clear(transparent ? new Color4(0, 0, 0, 0) : new Color4(.025f, .03f, .033f, 1));
        Frames++;
        Span<float> xs = stackalloc float[4];
        Span<float> ys = stackalloc float[4];
        ReadOnlySpan<float> cuts = stackalloc float[]
        {
            0,
            16,
            48,
            64
        };
        for (int index = 0; index < ops.Count; index++)
        {
            var op = ops[index];
            var b = ScrollGeometry.Screen(op.Bounds, op.Scrollable, scroll);
            if (!op.VisibleFor(hover, focus) || !Intersects(b, dirty) || (op.Clip is Rect visible && !Intersects(b, visible)))
                continue;
            Rect? localClip = op.ContentClip is Rect logical ? ScrollGeometry.Screen(logical, op.Scrollable, scroll) : null;
            if (localClip is Rect local && !Intersects(b, local))
                continue;
            var c = op.Color;
            float alpha = c.A * (op.Surface ? surfaceFade : op.Fixed ? 1 : fade);
            if (op.Dialog) alpha *= dialogFade;
            if (op.Kind == Ink.Dim)
                alpha *= dim;
            if (op.Pulse)
                alpha *= .75f + .25f * MathF.Cos(time * MathF.Tau);
            _brush.Color = new Color4(c.R, c.G, c.B, alpha);
            if (op.Clip is Rect clip)
                _target.PushAxisAlignedClip(new Vortice.RawRectF(clip.Left, clip.Top, clip.Right, clip.Bottom), AntialiasMode.PerPrimitive);
            if (localClip is Rect inner)
                _target.PushAxisAlignedClip(new Vortice.RawRectF(inner.Left, inner.Top, inner.Right, inner.Bottom), AntialiasMode.PerPrimitive);
            switch (op.Kind)
            {
                case Ink.SoftSurface:
                    var surface = Bitmap(op.Image);
                    if (surface == null)
                        break;
                    float ex = Math.Min(26, b.Width / 2), ey = Math.Min(20, b.Height / 2);
                    xs[0] = b.Left;
                    xs[1] = b.Left + ex;
                    xs[2] = b.Right - ex;
                    xs[3] = b.Right;
                    ys[0] = b.Top;
                    ys[1] = b.Top + ey;
                    ys[2] = b.Bottom - ey;
                    ys[3] = b.Bottom;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            _target.DrawBitmap(surface, new Rect(xs[sx], ys[sy], xs[sx + 1] - xs[sx], ys[sy + 1] - ys[sy]), alpha, BitmapInterpolationMode.Linear, new Rect(cuts[sx], cuts[sy], cuts[sx + 1] - cuts[sx], cuts[sy + 1] - cuts[sy]));
                    break;
                case Ink.ControlSurface:
                    if (_controlSurface == null)
                    {
                        using var stops = _target.CreateGradientStopCollection(new[] { new GradientStop(0, new Color4(12 / 255f, 14 / 255f, 15 / 255f, 0)), new GradientStop(.1f, new Color4(12 / 255f, 14 / 255f, 15 / 255f, 245 / 255f)), new GradientStop(.9f, new Color4(12 / 255f, 14 / 255f, 15 / 255f, 245 / 255f)), new GradientStop(1, new Color4(12 / 255f, 14 / 255f, 15 / 255f, 0)) });
                        _controlSurface = _target.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(), new Vector2(1, 0)), stops);
                    }

                    _controlSurface.StartPoint = new Vector2(b.Left, b.Top);
                    _controlSurface.EndPoint = new Vector2(b.Right, b.Top);
                    _controlSurface.Opacity = alpha;
                    _target.FillRectangle(b, _controlSurface);
                    break;
                case Ink.OutputThumb:
                    var outputThumb = ScrollGeometry.Thumb(b, outputMaximum, outputOffset);
                    _target.FillRoundedRectangle(new RoundedRectangle(new RectangleF(outputThumb.X, outputThumb.Y, outputThumb.Width, outputThumb.Height), 3, 3), _brush);
                    break;
                case Ink.ScrollThumb:
                    var thumb = ScrollGeometry.Thumb(b, scrollMax, scroll);
                    _target.FillRoundedRectangle(new RoundedRectangle(new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height), 4, 4), _brush);
                    break;
                case Ink.Dim:
                case Ink.Fill:
                    _target.FillRectangle(b, _brush);
                    break;
                case Ink.Outline:
                    _target.DrawRoundedRectangle(new RoundedRectangle(new RectangleF(b.X, b.Y, b.Width, b.Height), op.Radius, op.Radius), _brush, 1);
                    break;
                case Ink.Round:
                    _target.FillRoundedRectangle(new RoundedRectangle(new RectangleF(b.X, b.Y, b.Width, b.Height), op.Radius, op.Radius), _brush);
                    break;
                case Ink.Line:
                    _target.DrawLine(new Vector2(b.Left, b.Top), new Vector2(b.Right, b.Top), _brush, Math.Max(1, b.Height));
                    break;
                case Ink.Text:
                    _target.DrawTextLayout(new Vector2(b.X, b.Y), _layouts.Get(TextKey.From(op)), _brush, op.Emoji ? DrawTextOptions.Clip | DrawTextOptions.EnableColorFont : DrawTextOptions.Clip);
                    break;
                case Ink.Shadow:
                    if (_shadow == null)
                    {
                        using var stops = _target.CreateGradientStopCollection(new[] { new GradientStop(0, new Color4(20 / 255f, 22 / 255f, 23 / 255f, 1)), new GradientStop(.55f, new Color4(20 / 255f, 22 / 255f, 23 / 255f, .9f)), new GradientStop(1, new Color4(20 / 255f, 22 / 255f, 23 / 255f, 0)) });
                        _shadow = _target.CreateRadialGradientBrush(new RadialGradientBrushProperties(new Vector2(), new Vector2(), 1, 1), stops);
                    }

                    _shadow.Center = b.Center;
                    _shadow.RadiusX = b.Width / 2;
                    _shadow.RadiusY = b.Height / 2;
                    _shadow.Opacity = alpha;
                    _target.FillEllipse(new Ellipse(b.Center, b.Width / 2, b.Height / 2), _shadow);
                    break;
                case Ink.Glow:
                    var glow = Bitmap("menu-glow-half-joined");
                    if (glow != null)
                        _target.DrawBitmap(glow, b, alpha, BitmapInterpolationMode.Linear, new Rect(0, 0, glow.Size.Width, glow.Size.Height));
                    break;
                default:
                    var image = Bitmap(op.Image);
                    if (image == null)
                        break;
                    var old = _target.Transform;
                    if (op.Flip || op.FlipY)
                        _target.Transform = new Matrix3x2(op.Flip ? -1 : 1, 0, 0, op.FlipY ? -1 : 1, op.Flip ? b.Left + b.Right : 0, op.FlipY ? b.Top + b.Bottom : 0);
                    if (op.Kind == Ink.RoundImage)
                    {
                        _panel ??= _target.CreateBitmapBrush(image, new BitmapBrushProperties(ExtendMode.Clamp, ExtendMode.Clamp, BitmapInterpolationMode.Linear), null);
                        _panel.Bitmap = image;
                        _panel.Opacity = alpha;
                        _panel.Transform = Matrix3x2.CreateScale(b.Width / image.Size.Width, b.Height / image.Size.Height) * Matrix3x2.CreateTranslation(b.X, b.Y);
                        _target.FillRoundedRectangle(new RoundedRectangle(new RectangleF(b.X, b.Y, b.Width, b.Height), op.Radius, op.Radius), _panel);
                    }
                    else if (op.Source is Rect source)
                        _target.DrawBitmap(image, b, alpha, BitmapInterpolationMode.Linear, source);
                    else
                        _target.DrawBitmap(image, b, alpha, BitmapInterpolationMode.Linear, new Rect(0, 0, image.Size.Width, image.Size.Height));
                    _target.Transform = old;
                    break;
            }

            if (localClip != null)
                _target.PopAxisAlignedClip();
            if (op.Clip != null)
                _target.PopAxisAlignedClip();
        }

        if(navigationFocus is Rect focusBox && Intersects(focusBox,dirty))
        {
            var glow=Bitmap("menu-glow-half-joined");
            if(glow!=null)_target.DrawBitmap(glow,focusBox,.25f*navigationOpacity,BitmapInterpolationMode.Linear,
                new Rect(0,0,glow.Size.Width,glow.Size.Height));
        }
        _target.PopAxisAlignedClip();
        var result = _target.EndDraw();
        if (result.Failure)
            ResetDevice();
    }

    public void ImageCover(List<Op> ops, Rect bounds, string asset, float alpha = 1, float centerX = -1, float centerY = -1)
    {
        var bitmap = Bitmap(asset);
        if (bitmap == null)
            return;
        var size = bitmap.Size;
        var crop = WallpaperFraming.Crop(size.Width, size.Height, bounds.Width, bounds.Height, centerX, centerY);
        ops.Add(new(Ink.Image, bounds, new Color4(1, 1, 1, alpha), Image: asset, Source: crop));
    }

    void ResetDevice()
    {
        _controlSurface?.Dispose();
        _controlSurface = null;
        _shadow?.Dispose();
        _shadow = null;
        _panel?.Dispose();
        _panel = null;
        foreach (var b in _images.Values)
            b.Dispose();
        _images.Clear();
        _missingImages.Clear();
        _brush?.Dispose();
        _brush = null;
        _target?.Dispose();
        _target = null;
    }

    public void Dispose()
    {
        ResetDevice();
        _layouts.Dispose();
        foreach (var f in _formats.Values)
            f.Dispose();
        _formats.Clear();
        _fonts?.Dispose();
        _wic.Dispose();
        _write.Dispose();
        _factory.Dispose();
    }
}
