using System.Globalization;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    bool _searchActive, _appendBinding;
    string? _padCapture;
    HashSet<string> _padHeld = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Rect> _modalButtons = new();
    readonly List<Action> _modalActions = new();
    void DrawReferenceControls(float x, float width, ref float y)
    {
        // ControlsPage: one full-width input card, a tight profile row, then its wrap toolbar.
        float gap = 28 * _scale, half = (width - gap) / 2;
        Button("Keyboard/Mouse", new Rect(x + 14 * _scale, y + 7 * _scale, half - 14 * _scale, 50 * _scale), "keyboard", () =>
        {
            SdlPad.Stop();
            _profile.Input("keyboard_mouse");
            _pageRows = null;
            Rebuild();
        }, _profile.InputDevice == "keyboard_mouse", font: 22);
        Button("Gamepad", new Rect(x + half + gap, y + 7 * _scale, half - 14 * _scale, 50 * _scale), "gamepad", () =>
        {
            _profile.Input("controller");
            _preferredPads = null;
            _pageRows = null;
            Rebuild();
        }, _profile.InputDevice == "controller", font: 22);
        y += 68 * _scale;
        var rows = PageCatalog.Build(3, _profile);
        var profile = rows.First(o => o.Key == "native-profile");
        float name = _canvas.Measure(UiText.T("Profile"), ReferenceLayout.HeadingFont * _scale).X + 32 * _scale;
        float resetAllWidth = _canvas.Measure(UiText.T("Reset all"), 16 * _scale).X + 36 * _scale;
        Symbol("\uE77B", new Rect(x + 10 * _scale, y + 7 * _scale, 26 * _scale, 38 * _scale), Gold, 20);
        TextAt("Profile", new Rect(x + 42 * _scale, y + 7 * _scale, name - 30 * _scale, 38 * _scale),
            ReferenceLayout.HeadingFont, color: new Color4(215 / 255f, 216 / 255f, 213 / 255f, 1));
        Button("Reset all", new Rect(x + width - resetAllWidth, y + 7 * _scale, resetAllWidth, 38 * _scale),
            "controls-reset-all", () => AskControlReset(true), font: 16);
        bool showPreferred = _profile.InputDevice == "controller";
        float preferredColumn = (width - 28 * _scale) / 2;
        float preferredHeadingWidth = Math.Max(50 * _scale, preferredColumn - resetAllWidth - 14 * _scale);
        float headerHeight = showPreferred ? Math.Max(38 * _scale,
            WrappedHeight("Preferred gamepad", ReferenceLayout.HeadingFont, preferredHeadingWidth)) : 38 * _scale;
        float remaining = width - name - resetAllWidth - 86 * _scale;
        bool stackedProfile = showPreferred || remaining < 170 * _scale;
        var selector = new Rect(stackedProfile ? x + 10 * _scale : x + 12 * _scale + name,
            showPreferred ? y + headerHeight + 13 * _scale : y + (stackedProfile ? 51 : 7) * _scale,
            showPreferred ? preferredColumn - 50 * _scale : stackedProfile ? width - 62 * _scale : Math.Min(260 * _scale, remaining), 38 * _scale);
        string current = _profile.Str("controls_profile." + _profile.InputDevice, profile.Default);
        int index = _hits.Count;
        ControlFrame(selector, index);
        TextAt(current, new Rect(selector.X + 26 * _scale, selector.Y, selector.Width - 52 * _scale, selector.Height), 18.375f, true, translate: false);
        TextAt("⌄", new Rect(selector.Right - 24 * _scale, selector.Y, 20 * _scale, selector.Height), 18.375f, true, Gold);
        HitAt(selector, "native-profile", v => Change(profile, v, ScreenRect(selector, true)), current == ControlProfiles.DarkSouls3Name ? ControlProfiles.DarkSouls3Help : "", profile);
        Button("", new Rect(selector.Right + 6 * _scale, selector.Y, 34 * _scale, 38 * _scale), "profile-delete", () => PageAction("profile-delete"), icon: "\uE74D");
        float headerBottom = selector.Bottom;
        if (showPreferred)
        {
            float preferredX = x + preferredColumn + 28 * _scale;
            DrawPreferredGamepad(preferredX, y, preferredColumn - 10 * _scale, headerHeight, preferredHeadingWidth);
            headerBottom += 6 * _scale + WrappedHeight(PreferredGamepads.Help, 13.125f, preferredColumn - 10 * _scale, detail: true);
        }
        Line(x, headerBottom + 9 * _scale, width, new Color4(85 / 255f, 89 / 255f, 87 / 255f, .65f));
        y = headerBottom + 13 * _scale;
        float tx = x + 10 * _scale, toolbarHeight = 36 * _scale;
        foreach (var option in rows.Where(o => o.Kind == OptionKind.Action && o.Key != "action:profile-delete"))
        {
            float w = _canvas.Measure(UiText.T(option.Title), 18.375f * _scale).X + 44 * _scale;
            if (tx + w > x + width)
            {
                tx = x + 10 * _scale;
                y += toolbarHeight + 4 * _scale;
            }

            var entry = option;
            string glyph = entry.Key switch
            {
                "action:profile-save" => "\uE74E",
                "action:profile-copy" => "\uE8C8",
                "action:profile-export" => "\uE74A",
                "action:profile-import" => "\uE896",
                _ => "\uE71B"
            };
            Button(entry.Title, new Rect(tx, y, w, toolbarHeight), entry.Key, () => PageAction(entry.Key[7..]), icon: glyph);
            tx += w + 6 * _scale;
        }

        y += toolbarHeight + 16 * _scale;
        var values = rows.Where(o => o.Kind != OptionKind.Action && o.Key != "native-profile").ToArray();
        if (_profile.InputDevice == "keyboard_mouse")
        {
            var main = values.Where(o => o.Key is not ("stick_turn_rate" or "stick_circular" or "hide_cursor")).ToArray();
            DrawReferenceRows(main, x, width, ref y, true);
            DrawReferenceRows(values.Where(o => o.Key is "stick_turn_rate" or "stick_circular").ToArray(), x, width, ref y, true);
            DrawReferenceRows(values.Where(o => o.Key == "hide_cursor").ToArray(), x, width, ref y, false);
        }
        else
            DrawReferenceRows(values, x, width, ref y, true);
        y += 16 * _scale;
    }

    void DrawReferenceRows(IReadOnlyList<Option> rows, float x, float width, ref float y, bool split)
    {
        int columns = split && width >= 760 ? 2 : 1;
        float cell = (width - (columns - 1) * 24 * _scale) / columns;
        for (int i = 0; i < rows.Count; i += columns)
        {
            if (columns == 2 && rows[i].Key != "object_motion" && i + 1 < rows.Count && rows[i + 1].Key == "object_motion")
            {
                float singleHeight = OptionHeight(rows[i], cell);
                DrawOption(rows[i], new Rect(x, y, cell, singleHeight));
                y += singleHeight + 2 * _scale;
                i--;
                continue;
            }
            float height = 0;
            for (int c = 0; c < columns && i + c < rows.Count; c++)
                height = Math.Max(height, OptionHeight(rows[i + c], cell));
            for (int c = 0; c < columns && i + c < rows.Count; c++)
                DrawOption(rows[i + c], new Rect(x + c * (cell + 24 * _scale), y, cell, height));
            y += height + 2 * _scale;
        }
    }

    void DrawReferenceSections(IReadOnlyList<Option> options, float x, float width, ref float y)
    {
        var groups = options.GroupBy(o => o.Section == "DLSS options" ? "Upscaler" : o.Section).ToArray();
        bool two = width >= ReferenceLayout.SplitThreshold && _page != 2;
        float column = two ? (width - 24 * _scale) / 2 : width;
        for (int g = 0; g < groups.Length;)
        {
            var group = groups[g];
            bool full = !two || group.Count() > 7 || group.Key is "Engine preset" or "Game fixes" or "Screen" or "Upscaler" or "Diagnostics" or "Files" or "Connection";
            if (full)
            {
                DrawReferenceSection(group.Key, group.ToArray(), x, width, ref y, group.Key is not ("Engine preset" or "Game fixes") && (group.Count() > 7 || group.Key is "Screen" or "Upscaler" or "Diagnostics" or "Files" or "Connection"));
                g++;
            }
            else
            {
                float left = y, right = y;
                DrawReferenceSection(group.Key, group.ToArray(), x, column, ref left, false);
                g++;
                if (g < groups.Length && groups[g].Count() <= 7 && groups[g].Key is not ("Engine preset" or "Game fixes" or "Screen" or "Upscaler" or "Diagnostics" or "Files" or "Connection"))
                {
                    DrawReferenceSection(groups[g].Key, groups[g].ToArray(), x + column + 24 * _scale, column, ref right, false);
                    g++;
                }

                y = Math.Max(left, right);
            }
        }
    }

    void DrawReferenceSection(string title, Option[] rows, float x, float width, ref float y, bool split)
    {
        if (title.Length > 0)
        {
            SectionHeading(title, x, width, y);
            y += 50 * _scale;
        }

        y += 8 * _scale;
        DrawReferenceRows(rows, x + 10 * _scale, width - 20 * _scale, ref y, split);
        y += 28 * _scale;
    }

    void DrawBindings(float x, float width, ref float y)
    {
        y += 24 * _scale;
        float bindingsTop = y;
        float heading = _canvas.Measure(UiText.T("Keybindings"), 21.875f * _scale).X + 52 * _scale;
        Symbol(SectionIcons.For(_page, "Keybindings"), new Rect(x + 10 * _scale, y, 26 * _scale, 42 * _scale), Gold, 20);
        TextAt("Keybindings", new Rect(x + 42 * _scale, y, heading - 32 * _scale, 42 * _scale), 21.875f);
        var search = new Rect(x + heading + 14 * _scale, y, Math.Min(240 * _scale, Math.Max(100, width - heading - 140 * _scale)), 42 * _scale);
        Fill(search, new Color4(16 / 255f, 19 / 255f, 21 / 255f, 1));
        Line(search.X, search.Bottom - 1, search.Width, new Color4(115 / 255f, 120 / 255f, 117 / 255f, 1));
        TextAt(_filter.Length > 0 ? _filter + (_searchActive ? "|" : "") : _searchActive ? "|" : "Filter actions or keys", new Rect(search.X + 10 * _scale, search.Y, search.Width - 20 * _scale, search.Height), 16, color: Text, detail: true, translate: _filter.Length == 0);
        HitAt(search, "binding-search", _ =>
        {
            _searchActive = true;
            Rebuild();
        });
        float resetWidth = _canvas.Measure(UiText.T("Reset"), 16 * _scale).X + 36 * _scale;
        Button("Reset", new Rect(x + width - resetWidth, y, resetWidth, 42 * _scale), "reset-bindings",
            () => AskControlReset(false), font: 16);
        y += 50 * _scale;
        var cols = ReferenceLayout.Bindings(width / _scale);
        float action = cols.Action * _scale, keyboard = cols.Keyboard * _scale, pad = cols.Gamepad * _scale;
        float kx = x + action, px = kx + keyboard, rx = px + pad;
        TextAt("Action", new Rect(x + 4 * _scale, y, action - 12 * _scale, 28 * _scale), 15, bold: true);
        TextAt("Keyboard & mouse", new Rect(kx + 10 * _scale, y, keyboard - 14 * _scale, 28 * _scale), 15, bold: true);
        TextAt("Gamepad", new Rect(px + 10 * _scale, y, pad - 10 * _scale, 28 * _scale), 15, bold: true);
        y += 34 * _scale;
        string group = "";
        foreach (var c in InputMap.Controls)
        {
            string keys = string.Join(", ", Bindings.Get(_profile.Ini, c).Select(InputMap.Display));
            if (_filter.Length > 0 && !($"{UiText.T(c.Label)} {keys} {UiText.T(c.Hint)}".Contains(_filter, StringComparison.CurrentCultureIgnoreCase)))
                continue;
            if (group != c.Group)
            {
                group = c.Group;
                TextAt(UiText.T(group).ToUpper(CultureInfo.GetCultureInfo(UiText.Locale)), new Rect(x + 4 * _scale, y + 14 * _scale, width, 22 * _scale), 14, color: Muted, bold: true, translate: false);
                y += 40 * _scale;
            }

            TextAt(c.Label, new Rect(x + 4 * _scale, y, action - 12 * _scale, 44 * _scale), 16);
            float keyWidth = Math.Min(keyboard - 8 * _scale, BindingWidth(c));
            BindingButton(c, keys, new Rect(kx + 4 * _scale, y + 4 * _scale, keyWidth, 36 * _scale));
            string? input = PadMap.InputFor(c);
            if (input != null)
            {
                string label = PadMap.DisplayLabel(PadMap.Main(_profile.Ini, input));
                float size = Math.Min(pad - 10 * _scale, _canvas.Measure(label, 15 * _scale).X + 38 * _scale);
                Button(label, new Rect(px + 10 * _scale, y + 4 * _scale, size, 36 * _scale), "pad-" + input, () =>
                {
                    _padCapture = input;
                    _dialogTitle = c.Label;
                    _padHeld = SdlPad.Available ? SdlPad.Held() : new();
                    _searchActive = false;
                    Rebuild();
                }, font: 15, icon: "\uE7FC");
            }
            else
                TextAt(c.Pad ?? "—", new Rect(px + 18 * _scale, y, pad - 18 * _scale, 44 * _scale), 15);
            if (!Bindings.IsDefault(_profile.Ini, c) || (input != null && !PadMap.IsDefault(_profile.Ini, input)))
                Button("", new Rect(rx, y + 4 * _scale, 36 * _scale, 36 * _scale), "reset-" + c.Name, () =>
                {
                    _profile.EditSource((s, ini) =>
                    {
                        Bindings.Set(ini, c, InputMap.Split(c.Default));
                        if (input != null)
                            PadMap.Reset(ini, input);
                    });
                    Rebuild();
                }, font: 15, icon: "\uE7A7");
            y += 46 * _scale;
        }

        // Keep the search header at its current screen position as results shrink.
        // Only filtered content needs trailing room; clearing the query restores real rows.
        y = ReferenceLayout.FilteredExtent(y, bindingsTop, _viewport.Height, _filter.Length > 0);
    }

    void Symbol(string glyph, Rect bounds, float size, Color4 color) => Add(new(Ink.Text, bounds, color, glyph, size, Center: true, Symbol: true));
    float BindingWidth(ControlDef control)
    {
        var values = Bindings.Get(_profile.Ini, control);
        float width = 18 * _scale;
        if (values.Count == 0)
            return width + _canvas.Measure(UiText.T("Not bound"), 15 * _scale).X;
        for (int i = 0; i < values.Count; i++)
        {
            var atoms = BindingExpressions.Atoms(values[i]);
            bool keyboard = atoms.Any(a => !InputMap.IsMouse(a) && !(a.Length == 1 && char.IsLetterOrDigit(a[0]))) || values[i].Contains('+');
            bool mouse = atoms.Any(InputMap.IsMouse);
            width += _canvas.Measure(InputMap.Display(values[i]), 15 * _scale).X + ((keyboard ? 21 : 0) + (mouse ? 21 : 0)) * _scale;
            if (i > 0)
                width += _canvas.Measure(", ", 15 * _scale).X;
        }

        return width;
    }

    void BindingButton(ControlDef control, string label, Rect b)
    {
        var values = Bindings.Get(_profile.Ini, control);
        int index = _hits.Count;
        bool enabled = _profile.Ini.Get("pc_controls", "1") != "0";
        ControlFrame(b, index, enabled: enabled);
        HitAt(b, "bind-" + control.Name, _ =>
        {
            _binding = control;
            _appendBinding = false;
            _held.Clear();
            _chord.Clear();
            _searchActive = false;
            Rebuild();
        }, enabled: enabled);
        if (values.Count == 0)
        {
            TextAt("Not bound", b, 15, true, enabled ? Text : Muted);
            return;
        }

        float left = b.X + Math.Max(8 * _scale, (b.Width - BindingWidth(control) + 18 * _scale) / 2);
        for (int i = 0; i < values.Count; i++)
        {
            string value = values[i];
            var atoms = BindingExpressions.Atoms(value);
            bool keyboard = atoms.Any(a => !InputMap.IsMouse(a) && !(a.Length == 1 && char.IsLetterOrDigit(a[0]))) || value.Contains('+');
            bool mouse = atoms.Any(InputMap.IsMouse);
            if (i > 0)
            {
                float comma = _canvas.Measure(", ", 15 * _scale).X;
                TextAt(", ", new Rect(left, b.Y, comma, b.Height), 15, translate: false);
                left += comma;
            }

            if (keyboard)
            {
                DrawKeyIcon(new Rect(left, b.Y + (b.Height - 15 * _scale) / 2, 16 * _scale, 15 * _scale), false);
                left += 21 * _scale;
            }

            if (mouse)
            {
                DrawKeyIcon(new Rect(left, b.Y + (b.Height - 15 * _scale) / 2, 16 * _scale, 15 * _scale), true);
                left += 21 * _scale;
            }

            string text = InputMap.Display(value);
            float width = _canvas.Measure(text, 15 * _scale).X;
            TextAt(text, new Rect(left, b.Y, Math.Min(width, Math.Max(1, b.Right - left - 8 * _scale)), b.Height), 15, true, enabled ? Text : Muted, translate: false, weight: Bindings.IsDefault(_profile.Ini, control) ? 400 : 600);
            left += width;
        }
    }

    void DrawKeyIcon(Rect b, bool mouse)
    {
        if (mouse)
        {
            Add(new(Ink.Outline, new Rect(b.X + 3 * _scale, b.Y, b.Width - 6 * _scale, b.Height), Text, Radius: 5 * _scale));
            Fill(new Rect(b.X + 8 * _scale, b.Y, 1, 6 * _scale), Text);
            Line(b.X + 3 * _scale, b.Y + 6 * _scale, 10 * _scale, Text);
        }
        else
        {
            Add(new(Ink.Outline, new Rect(b.X, b.Y + 3 * _scale, b.Width, 10 * _scale), Text));
            for (int row = 0; row < 2; row++)
                for (int key = 0; key < 3; key++)
                    Line(b.X + (3 + 4 * key) * _scale, b.Y + (6 + 3 * row) * _scale, 2 * _scale, Text);
            Line(b.X + 4 * _scale, b.Y + 11 * _scale, 8 * _scale, Text);
        }
    }

    void EndReferenceCapture()
    {
        _binding = null;
        _padCapture = null;
        _appendBinding = false;
        _held.Clear();
        _chord.Clear();
        _modalButtons.Clear();
        _modalActions.Clear();
        SdlPad.Stop();
        Rebuild();
    }

    bool ActivateModal(float x, float y)
    {
        for (int i = 0; i < _modalButtons.Count; i++)
            if (_modalButtons[i].Contains(x, y))
            {
                Run(_modalActions[i]);
                return true;
            }

        return false;
    }

    void ModalButton(string title, Rect bounds, Action action)
    {
        _modalButtons.Add(bounds);
        _modalActions.Add(action);
        Button(title, bounds, "capture-" + _modalButtons.Count, action, font: 18.375f);
    }

    void DrawReferenceCapture()
    {
        _modalButtons.Clear();
        _modalActions.Clear();
        Fill(new Rect(_side, 0, _width - _side, _height), new Color4(16 / 255f, 16 / 255f, 18 / 255f, 176 / 255f));
        float width = Math.Min(500 * _scale, _width - _side - 32), height = _padCapture != null ? 470 * _scale : 310 * _scale;
        var b = new Rect(_side + (_width - _side - width) / 2, (_height - height) / 2, width, height);
        Fill(b, new Color4(25 / 255f, 27 / 255f, 28 / 255f, 1), 8 * _scale);
        ControlFrame(b);
        TextAt(_binding?.Label ?? _dialogTitle, new Rect(b.X + 22 * _scale, b.Y + 18 * _scale, b.Width - 44 * _scale, 32 * _scale), 21.875f);
        TextAt(_padCapture == null ? "Hold keys or mouse buttons together and release all to finish." : "Press the button on your controller, or choose one below.", new Rect(b.X + 22 * _scale, b.Y + 54 * _scale, b.Width - 44 * _scale, 42 * _scale), 16, color: Muted);
        var capture = new Rect(b.X + 22 * _scale, b.Y + 100 * _scale, b.Width - 44 * _scale, 110 * _scale);
        Fill(capture, new Color4(11 / 255f, 13 / 255f, 15 / 255f, 1), 6 * _scale);
        TextAt(_padCapture != null ? "Gamepad" : string.Join(" + ", _chord.Select(InputMap.Display)), capture, 16, true, Gold);
        float buttonsY = capture.Bottom + 12 * _scale;
        if (_padCapture != null)
        {
            float cx = b.X + 22 * _scale;
            foreach (var button in PadMap.Buttons.Where(v => v.Sdl.Length > 0))
            {
                float w = _canvas.Measure(PadMap.DisplayLabel(button.Sdl), 15 * _scale).X + 24 * _scale;
                if (cx + w > b.Right - 22 * _scale)
                {
                    cx = b.X + 22 * _scale;
                    buttonsY += 34 * _scale;
                }

                string sdl = button.Sdl;
                ModalButton(PadMap.DisplayLabel(sdl), new Rect(cx, buttonsY, w, 30 * _scale), () => CommitReferencePad(sdl));
                cx += w + 4 * _scale;
            }

            buttonsY += 40 * _scale;
        }

        float total = _padCapture == null ? 300 * _scale : 200 * _scale, left = b.Right - 22 * _scale - total;
        if (_padCapture == null)
        {
            ModalButton("Add input", new Rect(left, buttonsY, 96 * _scale, 38 * _scale), () =>
            {
                _appendBinding = true;
                Rebuild();
            });
            left += 102 * _scale;
        }

        ModalButton("Unbind", new Rect(left, buttonsY, 90 * _scale, 38 * _scale), () =>
        {
            if (_padCapture != null)
                CommitReferencePad("");
            else
            {
                var c = _binding!;
                _profile.EditSource((s, ini) => Bindings.Set(ini, c, Array.Empty<string>()));
                EndReferenceCapture();
            }
        });
        ModalButton("Cancel", new Rect(left + 96 * _scale, buttonsY, 90 * _scale, 38 * _scale), EndReferenceCapture);
    }

    void CommitReferencePad(string button)
    {
        if (_padCapture == null)
            return;
        string input = _padCapture;
        _profile.EditSource((s, ini) => PadMap.Assign(ini, input, button));
        EndReferenceCapture();
    }

    void PollReferencePad()
    {
        if (_padCapture == null || !SdlPad.Available)
            return;
        var held = SdlPad.Held();
        string? button = held.FirstOrDefault(b => !_padHeld.Contains(b));
        _padHeld = held;
        if (button != null)
            CommitReferencePad(button);
    }
}
