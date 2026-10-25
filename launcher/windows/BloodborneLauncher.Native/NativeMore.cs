using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using BloodborneLauncher.Native.Rendering;
using BloodborneLauncher.Native.Interaction;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    const string UpstreamRepository = "https://github.com/Awakenchan/LanternEngine";
    static string ForkCredit => UiText.Locale.Split('-')[0] switch
    {
        "es" => "Este launcher es un fork de Lantern Engine, de Awakenchan y sus colaboradores, con una interfaz y controles personalizados.",
        "fr" => "Ce launcher est un fork de Lantern Engine, créé par Awakenchan et ses contributeurs, avec une interface et des commandes personnalisées.",
        "de" => "Dieser Launcher ist ein Fork von Lantern Engine von Awakenchan und den Projektmitwirkenden, mit angepasster Oberfläche und Steuerung.",
        "it" => "Questo launcher è un fork di Lantern Engine di Awakenchan e dei suoi collaboratori, con interfaccia e comandi personalizzati.",
        "pt" => "Este launcher é um fork de Lantern Engine, de Awakenchan e seus colaboradores, com interface e controles personalizados.",
        "ru" => "Этот лаунчер — форк Lantern Engine от Awakenchan и участников проекта, с изменённым интерфейсом и управлением.",
        "ja" => "このランチャーは、Awakenchan とプロジェクトの貢献者による Lantern Engine のフォークで、独自のインターフェースと操作設定を追加しています。",
        _ => "This launcher is a fork of Lantern Engine by Awakenchan and its contributors, with a customized interface and controls."
    };

    void DrawLaunchAndAbout(Option[] rows, float x, float width, ref float y)
    {
        bool two = width >= ReferenceLayout.SplitThreshold;
        float column = two ? (width - 24 * _scale) / 2 : width, left = y, right = y;
        DrawReferenceSection("Launch", rows.Where(o => o.Section == "Launch").ToArray(), x, column, ref left, false);
        // About belongs to Launch's own column; it is not a separate empty Settings section.
        Symbol(SectionIcons.For(_page, "About"), new Rect(x + 12 * _scale, left - 8 * _scale, 24 * _scale, 30 * _scale), Gold, 18);
        TextAt("About", new Rect(x + 44 * _scale, left - 8 * _scale, column - 58 * _scale, 30 * _scale), 19.25f);
        left += 28 * _scale;
        float height = WrappedHeight(ForkCredit, 14, column - 28 * _scale, false, true);
        Wrapped(ForkCredit, new Rect(x + 14 * _scale, left, column - 28 * _scale, height), 14, Muted, false, true);
        left += height + 10 * _scale;
        foreach (string url in new[]
        {
            UpstreamRepository
        }

        )
        {
            float h = WrappedHeight(url, 13.125f, column - 28 * _scale, false, true);
            var bounds = new Rect(x + 14 * _scale, left, column - 28 * _scale, h + 6 * _scale);
            Wrapped(url, bounds, 13.125f, Gold, false, true);
            HitAt(bounds, "repository-" + url, _ => Run(() => OpenPath(url)), url);
            left += h + 12 * _scale;
        }

        if (!two)
            right = left;
        DrawReferenceSection("Performance", rows.Where(o => o.Section == "Performance").ToArray(), two ? x + column + 24 * _scale : x, column, ref right, false);
        y = Math.Max(left, right) + 18 * _scale;
        DrawReferenceSections(rows.Where(o => o.Section is not ("Launch" or "Performance")).ToArray(), x, width, ref y);
    }

    Dictionary<string, bool>? _patchEnabled;
    void RefreshReferenceMods()
    {
        _modEntries = _profile.WithSidecars(() => Mods.Load(_profile.ModsRoot));
        _patchEntries = Patches.Discover(_profile.PatchesRoot);
        var states = _profile.WithSidecars(Patches.LoadProfile);
        _patchEnabled = _patchEntries.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => Patches.IsOn(g.First(), states));
        _subpage = "Mods";
        _pageRows = null;
    }

    void ModFolder(string title, string key, string path, string status, float x, float width, ref float y)
    {
        ReferenceCard(title, status, "\uE8B7", x, width, ref y, new[] { new PageButton("Browse", key + "-browse", () =>
        {
            string? chosen = NativeDialogs.Folder(_window, UiText.T(title));
            if (chosen != null)
            {
                _profile.Set(key, chosen, true);
                RefreshReferenceMods();
                Rebuild();
            }
        }, "\uE8B7"), new PageButton("", key + "-open", () => OpenPath(path, true), "\uE8A7"), new PageButton("", key + "-refresh", () =>
        {
            RefreshReferenceMods();
            Rebuild();
        }, "\uE72C") }, true);
    }

    void DrawReferenceMods(float x, float width, ref float y)
    {
        if (_modEntries == null || _patchEntries == null || _patchEnabled == null)
            RefreshReferenceMods();
        PageGroup("Mods", x, width, ref y);
        Decorated(new Option("", "Load mods", "mods_enabled", OptionKind.Toggle, true, Default: "true", Hint: "When two mods replace the same file, the one lower in the list wins"), "\uEA86", x, width, ref y);
        string mods = _profile.ModsRoot;
        string status = _modEntries!.Count == 0 ? UiText.F($"{mods} — no mods") : UiText.F($"{mods} — {_modEntries.Count} mod{(_modEntries.Count == 1 ? "" : "s")}");
        ModFolder("Mods folder", "mods_dir", mods, status, x, width, ref y);
        for (int i = 0; i < _modEntries.Count; i++)
        {
            int index = i;
            var entry = _modEntries[i];
            var option = new Option("", entry.Name, "native-mod:" + i, OptionKind.Toggle, Default: entry.Enabled ? "1" : "0", Hint: UiText.F($"Load order {i + 1}"));
            float height = MoreToggleHeight(option, width - 54 * _scale, true);
            Symbol("\uE7B8", new Rect(x + 30 * _scale, y + 7 * _scale, 24 * _scale, 34 * _scale));
            DrawOption(option, new Rect(x + 54 * _scale, y, width - 54 * _scale, height));
            Button("", new Rect(x + width - 150 * _scale, y + 7 * _scale, 34 * _scale, 34 * _scale), "mod-up-" + i, () => MoveReferenceMod(index, -1), enabled: i > 0, icon: "\uE74A");
            Button("", new Rect(x + width - 112 * _scale, y + 7 * _scale, 34 * _scale, 34 * _scale), "mod-down-" + i, () => MoveReferenceMod(index, 1), enabled: i + 1 < _modEntries.Count, icon: "\uE74B");
            y += height + 4 * _scale;
        }

        y += 20 * _scale;
        PageGroup("Third-party patches", x, width, ref y);
        const string help = "shadPS4-format XML patches for version 01.09 from the patches folder, and the port's built-in ones (Bloodborne.xml, off until you switch them on; frame rate, effects and resolution have their own settings). Put your own patches in a file of their own: Bloodborne.xml is replaced on every update.";
        float hh = WrappedHeight(help, 14, width - 20 * _scale);
        Wrapped(help, new Rect(x + 10 * _scale, y, width - 20 * _scale, hh), 14, Muted);
        y += hh + 12 * _scale;
        string patches = _profile.PatchesRoot;
        ModFolder("Patches folder", "patches_dir", patches, _patchEntries!.Count == 0 ? UiText.F($"{patches} — no patches") : patches, x, width, ref y);
        for (int i = 0; i < _patchEntries.Count; i++)
        {
            var entry = _patchEntries[i];
            var option = new Option("", PatchTitles.Display(entry.Name, UiText.T), "native-patch:" + i, OptionKind.Toggle, Default: _patchEnabled![entry.Key] ? "1" : "0", Hint: entry.Note.Length > 0 ? entry.Note : entry.File + (entry.Author.Length > 0 ? " · by " + entry.Author : ""));
            float height = MoreToggleHeight(option, width - 54 * _scale, false);
            DrawOption(option, new Rect(x + 24 * _scale, y, width - 24 * _scale, height));
            y += height + 4 * _scale;
        }
    }

    float MoreToggleHeight(Option o, float width, bool reorder) => Math.Max(52 * _scale, WrappedHeight(o.Title, ReferenceLayout.RowFont, Math.Max(60 * _scale, width - (reorder ? 176 : 90) * _scale)) + 26 * _scale);
    void MoveReferenceMod(int index, int direction)
    {
        if (_modEntries == null || index + direction < 0 || index + direction >= _modEntries.Count)
            return;
        int next = index + direction;
        (_modEntries[index], _modEntries[next]) = (_modEntries[next], _modEntries[index]);
        _profile.WithSidecars(() =>
        {
            Mods.Save(_modEntries);
            return true;
        }, "mods.json");
        Rebuild();
    }

    readonly LiveOutputBuffer _output = new(gameOnly: true, requireSession: true);
    bool _followOutput = true, _outputFollowPending = true;
    float _outputOffset, _outputMax;
    Rect _outputBox;
    double _nextOutputPoll;
    readonly LogMemoryActivity _logMemory = new();
    string _runtimePathKey = "", _runtimeOutputPath = "";
    IEnumerable<string> SessionOutputCandidates()
    {
        string key = _profile.Str("user_dir") + "|" + _profile.Str("player_profile") + "|" + _profile.Str("port_dir");
        if (key != _runtimePathKey || _runtimeOutputPath.Length == 0)
        {
            _runtimePathKey = key;
            _runtimeOutputPath = Path.Combine(GameEnvironment.UserDir(_profile.ReadSettings()), "last_run.log");
        }

        yield return _runtimeOutputPath;
        string engine = _profile.Str("port_dir", Paths.EngineDir);
        string engineLog = Path.Combine(engine, "logs", "last_session.log");
        yield return engineLog;
        string configLog = Path.Combine(Paths.ConfigDir, "logs", "last_session.log");
        yield return configLog;
    }

    string SessionOutputPath() => SessionOutputCandidates().FirstOrDefault(File.Exists) ?? _runtimeOutputPath;
    void PollReferenceOutput(double now)
    {
        if (_page != 8 || _moreTab != 1 || now < _nextOutputPoll)
            return;
        _nextOutputPoll = now + .5;
        try
        {
            if (_output.Poll(SessionOutputPath()))
            {
                _outputFollowPending = _followOutput;
                // The retained page geometry does not depend on log contents.
                // Updating its native EDIT must not rebuild buttons/layouts.
                if (_textSpecs.TryGetValue("output-view", out var spec))
                {
                    _textSpecs["output-view"] = spec with { Value = _output.Text() };
                    _nativeTextNeedsSync = true;
                    SyncNativeText();
                }
                _logMemory.Changed(now);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    void DrawReferenceOutput(float x, float width, ref float y)
    {
        PageGroup("Game output", x, width, ref y);
        // Source OutputPage: Follow at the left, Copy / Export / Clear at the right.
        var commands = new[]
        {
            new PageButton("Copy", "output-copy", () => NativeDialogs.Copy(_window, _output.Text()), "\uE8C8"),
            new PageButton("Export…", "output-export", ExportReferenceOutput, "\uE74E"),
            new PageButton("Clear", "output-clear", () =>
            {
                _output.Clear();
                _outputOffset = 0;
                Rebuild();
            }, "\uE894")
        };
        float actions = commands.Sum(b => _canvas.Measure(UiText.T(b.Caption), 18.375f * _scale).X + 48 * _scale);
        bool stack = width - actions < 260 * _scale;
        var follow = new Option("", "Follow new output", "native-output-follow", OptionKind.Toggle, Default: _followOutput ? "1" : "0");
        float followWidth = stack ? width : width - actions - 12 * _scale;
        float h = OptionHeight(follow, followWidth);
        DrawOption(follow, new Rect(x, y, followWidth, h));
        float bx = stack ? x : x + width - actions, by = stack ? y + h : y + 7 * _scale;
        foreach (var b in commands)
        {
            float w = _canvas.Measure(UiText.T(b.Caption), 18.375f * _scale).X + 42 * _scale;
            var command = b;
            Button(b.Caption, new Rect(bx, by, w, 38 * _scale), b.Key, () => Run(command.Click), icon: b.Icon);
            bx += w + 6 * _scale;
        }

        y = Math.Max(y + h, by + 38 * _scale) + 8 * _scale;
        var onStart = new Option("", "Show this page when the game starts", "switch_to_output", OptionKind.Toggle, true, Default: "true");
        Decorated(onStart, "\uE756", x, width, ref y);
        float height = Math.Min(520 * _scale, Math.Max(180 * _scale, _viewport.Bottom - y - 14 * _scale));
        _outputBox = new Rect(x + 4 * _scale, y + 4 * _scale, width - 8 * _scale, height);
        Fill(_outputBox, new Color4(16 / 255f, 18 / 255f, 20 / 255f, .96f));
        Add(new(Ink.Outline, _outputBox, new Color4(.25f, .27f, .28f, 1)));
        Rect content = new(_outputBox.X + 12 * _scale, _outputBox.Y + 10 * _scale, _outputBox.Width - 34 * _scale, _outputBox.Height - 20 * _scale);
        QueueNativeText("output-view", content, _output.Text(), false, true, null);
        var vertical = new Rect(_outputBox.Right - 14 * _scale, content.Y, 6 * _scale, content.Height);
        Fill(vertical, new Color4(.06f, .07f, .075f, 1), 3 * _scale);
        Add(new(Ink.OutputThumb, vertical, new Color4(.36f, .37f, .38f, 1)));
        HitAt(vertical, "output-scroll", value => SetOutputOffset(Math.Clamp(value, 0, 1) * _outputMax));
        y = _outputBox.Bottom + 10 * _scale;
    }

    void SetOutputOffset(float offset)
    {
        _outputOffset = Math.Clamp(offset, 0, _outputMax);
        _outputScrollRequested = _nativeTextNeedsSync = true;
        SyncNativeText();
        InvalidateOutputThumb();
    }

    bool ScrollOutputWheel(float turns)
    {
        if (_page != 8 || _moreTab != 1 || !ScreenRect(_outputBox, true).Contains(_mouseX, _mouseY))
            return false;
        ScrollOutputWheelDelta((int)MathF.Round(turns * 120));
        return true;
    }

    void ExportReferenceOutput()
    {
        string? file = NativeDialogs.File(_window, true, UiText.T("Export the game output"), "Log files\0*.log\0Text files\0*.txt\0All files\0*.*\0\0", "log");
        if (file != null)
            File.WriteAllText(file, _output.Text());
    }
}
