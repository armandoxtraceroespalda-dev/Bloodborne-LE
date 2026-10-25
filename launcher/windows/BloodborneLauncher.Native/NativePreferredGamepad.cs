using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    IReadOnlyList<SdlPad.Device>? _preferredPads;
    void RefreshPreferredPads()
    {
        if (_game?.Running == true) return; // Never hold SDL resources while the game owns its pads.
        try { _preferredPads = SdlPad.Devices() ?? (IReadOnlyList<SdlPad.Device>)Array.Empty<SdlPad.Device>(); }
        catch (Exception error)
        {
            _preferredPads = Array.Empty<SdlPad.Device>();
            LauncherDiagnostics.Record("Gamepad discovery failed: " + error.Message, LogLevel.Warn);
        }
        finally { if (_padCapture == null) SdlPad.Stop(); }
    }

    void DrawPreferredGamepad(float x, float y, float width, float headingHeight, float headingWidth)
    {
        if (_preferredPads == null) RefreshPreferredPads();
        var devices = _preferredPads ?? Array.Empty<SdlPad.Device>();
        var option = PreferredGamepads.Option(devices, _profile.Str(PreferredGamepads.Key));
        Wrapped("Preferred gamepad", new Rect(x, y + 7 * _scale, headingWidth, headingHeight),
            ReferenceLayout.HeadingFont, new Color4(215 / 255f, 216 / 255f, 213 / 255f, 1));
        var selector = new Rect(x, y + headingHeight + 13 * _scale, width, 38 * _scale);
        int index = _hits.Count;
        ControlFrame(selector, index);
        TextAt(PreferredGamepads.Label(devices, _profile.Str(PreferredGamepads.Key)),
            new Rect(selector.X + 12 * _scale, selector.Y, selector.Width - 40 * _scale, selector.Height),
            18.375f, translate: false);
        TextAt("⌄", new Rect(selector.Right - 24 * _scale, selector.Y, 20 * _scale, selector.Height), 18.375f, true, Gold);
        HitAt(selector, option.Key, v => Change(option, v, ScreenRect(selector, true)), option: option);
        float helpHeight = WrappedHeight(PreferredGamepads.Help, 13.125f, width, detail: true);
        Wrapped(PreferredGamepads.Help, new Rect(x, selector.Bottom + 6 * _scale, width, helpHeight), 13.125f, Muted, detail: true);
    }
}
