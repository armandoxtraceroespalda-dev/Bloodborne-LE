using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    sealed record ControlReset(string Device, bool All);
    ControlReset? _resetPrompt;

    void AskControlReset(bool all)
    {
        _resetPrompt = new(_profile.InputDevice, all);
        OpenDialogAnimation();
        if (_searchActive) _dirty = true;
        _searchActive = false;
        HideNativeText();
        RefreshDialogSurface();
    }

    void CloseControlReset(bool apply) => CloseDialogAnimation(() => FinishControlReset(apply));

    void FinishControlReset(bool apply)
    {
        var prompt = _resetPrompt;
        _resetPrompt = null;
        if (apply && prompt != null)
            ControlDefaults.Reset(_profile, prompt.Device, prompt.All);
        if (apply) { _pageRows = null; ClearTooltip(); Rebuild(); _navigationMemory.Request(); }
        else RefreshDialogSurface();
    }

    void DrawControlReset()
    {
        _fixed = true;
        _scrollContent = false;
        _clip = null;
        float width = Math.Min(580 * _scale, DialogArea.Width - 48), height = Math.Min(218 * _scale, DialogArea.Height - 48);
        var box = DialogPlacement.Center(DialogArea, width, height);
        DialogBackdrop(box);
        var text = ControlResetText.For(_resetPrompt!.All, _resetPrompt.Device);
        DialogText(text.Title, new Rect(box.X + 24 * _scale, box.Y + 20 * _scale, width - 48 * _scale, 50 * _scale), 23, Text, false);
        DialogText(text.Body, new Rect(box.X + 56 * _scale, box.Y + 74 * _scale, width - 112 * _scale, height - 140 * _scale),
            18, Gold, false);
        var info = new Rect(box.Right - 56 * _scale, box.Y + 77 * _scale, 24 * _scale, 24 * _scale);
        Add(new(Ink.Outline, info, Gold, Radius: 12 * _scale));
        TextAt("!", info, 15, true, Gold, detail: true, translate: false);
        HitAt(info, "controls-reset-info", _ => { }, text.Tooltip);
        float button = Math.Min(190 * _scale, (width - 64 * _scale) / 2), top = box.Bottom - 58 * _scale;
        Button("Cancel", new Rect(box.Center.X - button - 8 * _scale, top, button, 38 * _scale), "controls-reset-cancel", () => CloseControlReset(false));
        Button("Reset", new Rect(box.Center.X + 8 * _scale, top, button, 38 * _scale), "controls-reset-accept", () => CloseControlReset(true));
    }
}
