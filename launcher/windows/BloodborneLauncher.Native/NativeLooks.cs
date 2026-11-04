using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using BloodborneLauncher.Native.Rendering;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    void ApplyNativeLook(string value)
    {
        Run(() =>
        {
            var look = value == GraphicsProfiles.Custom ? null : GraphicsProfiles.Resolve(value);
            if (value != GraphicsProfiles.Custom && look == null) return;
            GraphicsProfiles.Apply(_profile, value);
            _graphicsOptions = null;
        });
    }

    bool NativeLookAction(string action)
    {
        if (!action.StartsWith("look-", StringComparison.Ordinal)) return false;
        var selected = GraphicsProfiles.Resolve(GraphicsProfiles.Selection(_profile));
        var current = Looks.FromCurrent(_profile.Ini, selected?.Name ?? "Custom", selected?.Author ?? "");
        switch (action)
        {
            case "look-save":
                Edit("Save current", "", name =>
                {
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (GraphicsProfiles.Selection(_profile) == GraphicsProfiles.Custom) GraphicsProfiles.CaptureCustom(_profile);
                    var saved = Looks.FromCurrent(_profile.Ini, name, "");
                    Looks.Save(saved);
                    _profile.Set("native-look", GraphicsProfiles.Id(saved), true);
                    _graphicsOptions = null;
                });
                break;
            case "look-copy": NativeDialogs.Copy(_window, Looks.ToCode(current)); break;
            case "look-export": SaveJson(Looks.ToJson(current)); break;
            case "look-import": ImportJson(ImportNativeLook); break;
            case "look-paste": ImportNativeLook(NativeDialogs.Paste(_window)); break;
            case "look-delete":
                if (selected != null && !selected.BuiltIn && Confirm(UiText.T("Delete profile") + "\n" + selected.Name))
                {
                    GraphicsProfiles.Apply(_profile, GraphicsProfiles.Custom);
                    Looks.Delete(selected);
                    _profile.Set("native-look", GraphicsProfiles.Custom, true);
                }
                break;
        }
        _graphicsOptions = null;
        return true;
    }

    void ImportNativeLook(string text)
    {
        var look = Looks.Parse(text);
        if (!Confirm(UiText.T("Use") + "\n" + look.Name)) return;
        // An import can replace the currently selected saved profile.
        if (GraphicsProfiles.Selection(_profile) == GraphicsProfiles.Custom) GraphicsProfiles.CaptureCustom(_profile);
        Looks.Save(look);
        GraphicsProfiles.Apply(_profile, GraphicsProfiles.Id(look), preserveCustom: true);
        _graphicsOptions = null;
    }

    void DrawGraphicsProfiles(Option[] rows, float x, float width, ref float y)
    {
        SectionHeading("Graphics profiles", x, width, y);
        y += 50 * _scale;
        var geometry = GraphicsProfileLayout.Place(width / _scale);
        var option = rows.Single(row => row.Key == "native-look") with { Choices = GraphicsProfiles.Choices() };
        string value = _profile.Value(option);
        string caption = option.Choices!.FirstOrDefault(choice => choice.Value == value)?.Label ?? UiText.T("Custom");
        float labelWidth = geometry.Stacked ? _canvas.Measure(UiText.T("Profile"), ReferenceLayout.RowFont * _scale).X + 14 * _scale : 0;
        TextAt("Profile", new Rect(x + 10 * _scale, y + 3 * _scale,
            geometry.Stacked ? labelWidth : geometry.SelectorWidth * _scale - 20 * _scale, geometry.Stacked ? 38 * _scale : 28 * _scale), ReferenceLayout.RowFont);
        var selector = new Rect(x + 10 * _scale + labelWidth, y + (geometry.Stacked ? 3 : 36) * _scale,
            Math.Max(1, geometry.SelectorWidth * _scale - labelWidth - 20 * _scale), 38 * _scale);
        int hit = _hits.Count;
        Add(new(Ink.ControlSurface, selector, new(1, 1, 1, 1), Trigger: hit));
        _glows.Add(Add(new(Ink.Glow, selector, new(1, 1, 1, .9f), Trigger: hit)));
        TextAt(caption, new Rect(selector.X + 12 * _scale, selector.Y, Math.Max(1, selector.Width - 38 * _scale), selector.Height), ReferenceLayout.ActionFont, true, Text, translate: false);
        TextAt("⌄", new Rect(selector.Right - 25 * _scale, selector.Y, 20 * _scale, selector.Height), ReferenceLayout.ActionFont, true, Gold);
        HitAt(selector, option.Key, position => Change(option, position, ScreenRect(selector, true)), option.Choices!.FirstOrDefault(choice => choice.Value == value)?.Note ?? "", option: option);
        var actions = rows.Where(row => row.Kind == OptionKind.Action).ToArray();
        var selected = GraphicsProfiles.Resolve(value);
        for (int i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            string icon = action.Key switch
            {
                "action:look-save" => "\uE74E", "action:look-copy" => "\uE8C8",
                "action:look-export" => "\uEDE1", "action:look-import" => "\uE8B5",
                "action:look-paste" => "\uE77F", _ => "\uE74D"
            };
            bool enabled = action.Key != "action:look-delete" || selected is { BuiltIn: false };
            int actionHit = _hits.Count;
            var bounds = new Rect(x + (geometry.ActionsX + i % 3 * (geometry.ButtonWidth + geometry.Gap)) * _scale,
                y + ((geometry.Stacked ? 47 : 3) + i / 3 * 44) * _scale, geometry.ButtonWidth * _scale, 38 * _scale);
            float textWidth = _canvas.Measure(UiText.T(action.Title), ReferenceLayout.ActionFont * _scale).X;
            float font = ReferenceLayout.ActionFont * Math.Min(1, Math.Max(1, bounds.Width - 42 * _scale) / Math.Max(1, textWidth));
            Button(action.Title, bounds, action.Key, () => Run(() => PageAction(action.Key[7..])), enabled: enabled, icon: icon, font: font);
            if (_hits.Count > actionHit) _hits[actionHit] = _hits[actionHit] with { Hint = UiText.T(action.Title) };
        }
        FinishSectionOutline();
        y += (geometry.Stacked ? 140 : 96) * _scale;
    }
}
