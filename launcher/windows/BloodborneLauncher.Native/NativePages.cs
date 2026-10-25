using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    IReadOnlyList<Option>? _pageRows;
    string _subpage = "", _filter = "", _dialogTitle = "", _dialogText = "";
    Action<string>? _textCommit;
    bool _secret, _busy;
    readonly CheatAccess _cheats = new();
    ControlDef? _binding;
    readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _chord = new();
    readonly Queue<(Action Action, bool Final)> _completions = new();
    readonly CancellationTokenSource _cancel = new();
    List<string>? _logLines;
    TrophyTracker? _trophies;
    List<SaveBackups.Backup>? _restoreBackups;
    List<Mods.Entry>? _modEntries;
    List<Patches.Patch>? _patchEntries;
    const uint WorkerMessage = 0x8001;
    bool Confirm(string text) => ShowMessage(text, UiText.T("Confirm"), 0x24) == 6;
    void Edit(string title, string value, Action<string> commit, bool secret = false)
    {
        _dialogTitle = title;
        _dialogText = secret ? "" : value;
        _textCommit = commit;
        _secret = secret;
        Rebuild();
    }

    bool HandleText(char ch)
    {
        if (_dialogClosing || _messagePrompt != null) return true;
        if(_page==0 && _globalSearchActive && _textCommit==null && _binding==null && _editing==null)
        {
            if(ch=='\b')ChangeGlobalSearch(_globalQuery.Length>0?_globalQuery[..^1]:"");
            else if(!char.IsControl(ch))ChangeGlobalSearch(_globalQuery+ch);
            _focusGlobalAfterSync=true;
            return true;
        }
        if (ch == '\x16' && (_textCommit != null || _searchActive) && _binding == null && _padCapture == null)
        {
            string pasted = NativeDialogs.Paste(_window);
            if (_textCommit != null && _dialogText.Length + pasted.Length <= 1024 * 1024)
                _dialogText += pasted;
            else if (_searchActive)
                _filter = (_filter + pasted)[..Math.Min(200, _filter.Length + pasted.Length)];
            Rebuild();
            return true;
        }

        if (_searchActive && _textCommit == null && _binding == null && _padCapture == null)
        {
            if (ch == '\b')
            {
                if (_filter.Length > 0)
                    _filter = _filter[..^1];
            }
            else if (!char.IsControl(ch) && _filter.Length < 200)
                _filter += ch;
            Rebuild();
            return true;
        }

        if (_textCommit != null)
        {
            if (ch == '\b')
            {
                if (_dialogText.Length > 0)
                    _dialogText = _dialogText[..^1];
            }
            else if (!char.IsControl(ch) && _dialogText.Length < 32768)
                _dialogText += ch;
            Rebuild();
            return true;
        }

        if (_binding != null || _editing != null)
            return false;
        if (_cheats.Feed(ch.ToString()))
        {
            _settingsIndex = null;
            _profile.Set("cheats_unlocked", _cheats.Unlocked ? "true" : "false", true);
            if (!_cheats.Unlocked)
            {
                _profile.HideCheats();
                if (_page == 9)
                    Switch(4);
            }

            _profile.SavePreview();
            Rebuild();
        }

        return false;
    }

    bool EditKey(int key)
    {
        if (_textCommit == null)
            return false;
        if (key == 0x1B)
        {
            _textCommit = null;
            _dialogText = "";
            Rebuild();
            return true;
        }

        if (key == 0x0D)
        {
            var commit = _textCommit;
            string text = _dialogText;
            _textCommit = null;
            _dialogText = "";
            Run(() => commit(text));
            _pageRows = null;
            Rebuild();
        }

        return true;
    }

    void DrawEditor()
    {
        if (_binding != null || _padCapture != null)
        {
            DrawReferenceCapture();
            return;
        }

        _modalButtons.Clear();
        _modalActions.Clear();
        Fill(new Rect(_side, 32, _width - _side, _height - 32), new Color4(0, 0, 0, .85f));
        var b = new Rect(_side + 35, _height / 2 - 110, _width - _side - 70, 220);
        Fill(b, new Color4(.025f, .03f, .033f, 1), 10);
        TextAt(_binding == null ? _dialogTitle : _binding.Label, new Rect(b.X + 15, b.Y + 10, b.Width - 30, 42), 24, true);
        string text = _binding != null ? string.Join(" + ", _chord.Select(InputMap.Display)) : _secret ? new string ('•', _dialogText.Length) : _dialogText;
        TextAt(text + "|", new Rect(b.X + 20, b.Y + 62, b.Width - 40, 80), 20, true, Gold, detail: _binding == null, translate: false);
        TextAt(_binding != null ? "Press keys or mouse buttons; release all to finish. Delete clears. Esc cancels." : "Enter: apply · Esc: cancel", new Rect(b.X + 20, b.Y + 160, b.Width - 40, 50), 15, true, Muted, true);
    }

    void PageHeader(float x, float width, ref float y)
    {
        if (_subpage.Length > 0)
        {
            Button("Back", new Rect(x, y, 130 * _scale, 34 * _scale), "back", () =>
            {
                _subpage = "";
                _pageRows = null;
                _scroll = 0;
                Rebuild();
            });
            y += 48 * _scale;
        }

        if (_page == 3 && _subpage.Length == 0)
        {
            Button("Keyboard/Mouse", new Rect(x, y, width * .48f, 44 * _scale), "keyboard", () =>
            {
                _profile.Input("keyboard_mouse");
                _pageRows = null;
                Rebuild();
            }, _profile.InputDevice == "keyboard_mouse");
            Button("Gamepad", new Rect(x + width * .52f, y, width * .48f, 44 * _scale), "gamepad", () =>
            {
                _profile.Input("controller");
                _pageRows = null;
                Rebuild();
            }, _profile.InputDevice == "controller");
            y += 58 * _scale;
        }

        if (_subpage == "Logs" && _logLines != null)
            foreach (var line in _logLines)
            {
                TextAt(line, new Rect(x, y, width, 24 * _scale), 13, color: Gold, detail: true, translate: false);
                y += 25 * _scale;
            }
    }

    void DrawAchievements(float x, float width, ref float y)
    {
        if (_trophies == null)
        {
            _trophies = new();
            _trophies.SetUserDir(GameEnvironment.UserDir(_profile.SourceSettings()));
            _trophies.Poll(false);
        }

        TextAt($"{_trophies.Count}/{TrophyTracker.All.Length}", new Rect(x, y, width, 32 * _scale), 24, true, Gold, translate: false);
        y += 44 * _scale;
        foreach (var t in TrophyTracker.All)
        {
            bool earned = _trophies.Unlocked.ContainsKey(t.Id);
            TextAt((earned ? "✓ " : "— ") + UiText.T(t.Name), new Rect(x, y, width, 30 * _scale), 20, color: earned ? Gold : Muted, translate: false);
            TextAt(t.Description, new Rect(x, y + 30 * _scale, width, 46 * _scale), 15, color: Muted);
            y += 84 * _scale;
        }
    }

    void CaptureAtom(string atom, bool down)
    {
        if (_binding == null)
            return;
        if (down)
        {
            if (_held.Add(atom) && !_chord.Contains(atom, StringComparer.OrdinalIgnoreCase))
                _chord.Add(atom);
        }
        else
        {
            _held.Remove(atom);
            if (_held.Count == 0 && _chord.Count > 0)
            {
                var control = _binding;
                _binding = null;
                Run(() => _profile.EditSource((settings, ini) =>
                {
                    string chord = string.Join(" + ", _chord);
                    if (_appendBinding)
                        Bindings.Set(ini, control, Bindings.Get(ini, control).Append(chord));
                    else
                        Bindings.Assign(ini, control, chord, true);
                }));
                _appendBinding = false;
                _chord.Clear();
            }
        }

        Rebuild();
    }

    bool CaptureKey(int vk, bool down, nint flags)
    {
        if (_binding == null)
            return false;
        if (vk == 0x1B)
        {
            _binding = null;
            _held.Clear();
            _chord.Clear();
            Rebuild();
            return true;
        }

        if (vk == 0x2E && down)
        {
            _profile.EditSource((s, ini) => Bindings.Set(ini, _binding, Array.Empty<string>()));
            _binding = null;
            Rebuild();
            return true;
        }

        var atom = KeyboardNames.FromVirtualKey(vk, (long)flags);
        if (atom != null)
            CaptureAtom(atom, down);
        return true;
    }

    void PageAction(string action)
    {
        var settings = _profile.SourceSettings();
        switch (action)
        {
            case "profile-save":
                Edit("Save as", "", name =>
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        ControlProfiles.Save(_profile.CaptureProfile(name));
                });
                break;
            case "profile-copy":
                NativeDialogs.Copy(_window, ControlProfiles.ToJson(_profile.CaptureProfile("Custom")));
                break;
            case "profile-export":
                SaveJson(ControlProfiles.ToJson(_profile.CaptureProfile("Custom")));
                break;
            case "profile-import":
                ImportJson(json =>
                {
                    var p = ControlProfiles.Parse(json).Profile;
                    ControlProfiles.Save(p);
                    _profile.UseProfile(p);
                });
                break;
            case "profile-link":
                Edit("Import from link", "", url => Background(async () =>
                {
                    var p = await ControlProfiles.DownloadAsync(url);
                    return () =>
                    {
                        ControlProfiles.Save(p.Profile);
                        _profile.UseProfile(p.Profile);
                    };
                }));
                break;
            case "profile-delete":
                var selected = _profile.Profiles().FirstOrDefault(p => p.Name == _profile.Str("controls_profile." + _profile.InputDevice));
                if (selected != null && !selected.BuiltIn && Confirm(UiText.T("Delete this profile")))
                    ControlProfiles.Delete(selected);
                break;
            case "settings-share":
                SaveJson(_profile.ExportSettings());
                break;
            case "settings-import":
                ImportJson(json =>
                {
                    if (Confirm(UiText.T("Import your settings")))
                        _profile.ImportSettings(json);
                });
                break;
            case "player-new":
                Edit("New player profile", "", name => _profile.EditSource((s, ini) =>
                {
                    var clean = PlayerProfiles.Validate(s, name, out var error);
                    if (clean == null)
                        throw new InvalidOperationException(error);
                    PlayerProfiles.Add(s, clean);
                }));
                break;
            case "backup":
                _notice = SaveBackups.Create(settings) ?? UiText.T("No saves found.");
                break;
            case "backups-folder":
                OpenPath(SaveBackups.Folder(settings), true);
                break;
            case "restore-backup":
                _restoreBackups = SaveBackups.List(settings);
                _popup = new(new Option("", "Restore backup", "restore-backup", OptionKind.Choice, Choices: _restoreBackups.Select((b, i) => Choice.Literal(b.When.ToString("g"), i.ToString())).ToArray()), new Rect(_mouseX, _mouseY, 320, 32));
                break;
            case "settings-ini":
                OpenPath(_profile.OriginalIni);
                break;
            case "settings-config":
                OpenPath(_profile.ConfigPath);
                break;
            case "settings-cache":
                OpenPath(Path.Combine(Paths.DataDir, "cache"), true);
                break;
            case "logs":
                OpenPath(Path.Combine(Paths.DataDir, "logs"), true);
                break;
            case "view-log":
                _subpage = "Logs";
                _pageRows = Array.Empty<Option>();
                _scroll = 0;
                string dir = Path.Combine(Paths.DataDir, "logs");
                string? log = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                _logLines = log == null ? new()
                {
                    UiText.T("No log found.")
                }

                : ReadTail(log, 200);
                break;
            case "engine":
                ShowEngine();
                break;
            case "mods":
                ShowMods();
                break;
            case "patches":
                ShowPatches();
                break;
            case "online-test":
                string testServer = _profile.Ini.Get("online_pool", "huntersdream");
                Background(async () =>
                {
                    var results = await Online.CheckAsync(Online.GameServer(testServer));
                    return () => ShowMessage(string.Join("\n", results.Select(r => $"{r.Name}: {r.Detail}")), UiText.T("Connection"), 0);
                });
                break;
            case "online-link":
                LinkAccount();
                break;
            case "online-create":
                Edit("Username", "", name => Background(async () =>
                {
                    var a = await HuntersDream.CreateAccountAsync(name, _cancel.Token);
                    return () =>
                    {
                        StoreAccount(a.Name, a.Token);
                        ShowMessage(a.RecoveryCode, UiText.T("Recovery code"), 0);
                    };
                }));
                break;
            case "online-recover":
                Edit("Username", "", name => Edit("Recovery code", "", code => Background(async () =>
                {
                    var a = await HuntersDream.RecoverAsync(name, code, _cancel.Token);
                    return () => StoreAccount(a.Name, a.Token);
                })));
                break;
        }

        Rebuild();
    }

    void SaveJson(string json)
    {
        string? file = NativeDialogs.File(_window, true, UiText.T("Export file"));
        if (file != null)
            File.WriteAllText(file, json);
    }

    void ImportJson(Action<string> apply)
    {
        string? file = NativeDialogs.File(_window, false, UiText.T("Import file"));
        if (file != null)
        {
            if (new FileInfo(file).Length > 1024 * 1024)
                throw new InvalidOperationException("File is too large.");
            apply(File.ReadAllText(file));
        }
    }

    static void OpenPath(string path, bool folder = false)
    {
        if (folder)
            Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    static List<string> ReadTail(string path, int count)
    {
        var lines = new Queue<string>(count);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        while (reader.ReadLine()is { } line)
        {
            if (lines.Count == count)
                lines.Dequeue();
            lines.Enqueue(line.Length > 500 ? line[..500] : line);
        }

        return lines.ToList();
    }

    void Background(Func<Task<Action>> work)
    {
        if (_busy)
            return;
        _busy = true;
        _notice = UiText.T("Loading…");
        _ = Task.Run(async () =>
        {
            Action completion;
            try
            {
                completion = await work();
            }
            catch (Exception e)
            {
                string message = e.GetBaseException().Message;
                completion = () => _notice = message;
            }

            lock (_completions)
                _completions.Enqueue((completion, true));
            W.PostMessage(_window, WorkerMessage, 0, 0);
        });
    }

    void CompleteWorker()
    {
        lock (_completions)
            while (_completions.Count > 0)
            {
                var entry = _completions.Dequeue();
                if (entry.Final) { _busy = false; _notice = ""; }
                Run(entry.Action);
            }

        _pageRows = null;
        Rebuild();
    }

    void StoreAccount(string name, string token) => _profile.EditSource((s, ini) =>
    {
        s.Set("hd_name", name);
        s.HuntersDreamToken = token;
        ini.Set("online_pool", "huntersdream");
    });
    void LinkAccount()
    {
        var settings = _profile.SourceSettings();
        if (settings.HuntersDreamToken.Length > 0)
        {
            if (Confirm(UiText.T("Disconnect")))
                _profile.EditSource((s, ini) =>
                {
                    s.HuntersDreamToken = "";
                    s.Set("hd_name", "");
                    ini.Set("online_pool", "");
                });
            return;
        }

        Background(async () =>
        {
            var link = await HuntersDream.StartLinkAsync(_cancel.Token);
            lock (_completions)
                _completions.Enqueue((() =>
                {
                    NativeDialogs.Copy(_window, link.UserCode);
                    OpenPath(link.Page);
                    _notice = link.UserCode;
                }, false));
            W.PostMessage(_window, WorkerMessage, 0, 0);
            var account = await HuntersDream.WaitLinkAsync(link, _cancel.Token);
            return () => StoreAccount(account.Name, account.Token);
        });
    }

    void ShowEngine()
    {
        _subpage = "Engine";
        _scroll = 0;
        _pageRows = new[]
        {
            new Option("Engine preset", "Preset", "native-engine-preset", OptionKind.Choice,
                Setting: true, Choices: EnginePreset.Choices, Default: EnginePreset.New,
                Hint: "New is the current engine. Legacy runs it the way 0.9.33 did: try it if the game crashes or draws something wrong")
        }.Concat(EngineSwitches.All.Where(i => EngineSwitches.Groups.Contains(i.Group) && i.Group != EngineSwitches.Graphics && i.Env != "BB_GAMEPAD").Select(i => new Option(i.Group, i.Title, i.Key, i.Kind switch
        {
            SwitchKind.Toggle => OptionKind.Toggle,
            SwitchKind.Number => OptionKind.Slider,
            SwitchKind.Choice => OptionKind.Choice,
            _ => OptionKind.Text
        }, true, i.Options, i.Min, i.Max, i.Step, i.Default, Hint: EngineSwitches.Describe(i)))).ToArray();
    }

    void ShowMods()
    {
        _subpage = "Mods";
        _scroll = 0;
        _modEntries = _profile.WithSidecars(() => Mods.Load(_profile.ModsRoot));
        _pageRows = _modEntries.Select((e, i) => new Option("", e.Name, "native-mod:" + i, OptionKind.Toggle, Default: e.Enabled ? "1" : "0")).ToArray();
    }

    void ShowPatches()
    {
        _subpage = "Third-party patches";
        _scroll = 0;
        _patchEntries = Patches.Discover(_profile.PatchesRoot);
        var p = _profile.WithSidecars(Patches.LoadProfile);
        _pageRows = _patchEntries.Select((e, i) => new Option("", e.Name.Replace("(read notes)", "", StringComparison.OrdinalIgnoreCase).Trim(), "native-patch:" + i, OptionKind.Toggle, Default: Patches.IsOn(e, p) ? "1" : "0", Hint: e.Note)).ToArray();
    }
}

internal static class KeyboardNames
{
    public static string? FromVirtualKey(int vk, long flags)
    {
        if (vk >= 0x41 && vk <= 0x5A)
            return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39)
            return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87)
            return "F" + (vk - 0x6F);
        if (vk >= 0x60 && vk <= 0x69)
            return "Keypad " + (vk - 0x60);
        bool extended = (flags & 0x1000000) != 0;
        return vk switch
        {
            0x10 => ((flags >> 16) & 255) == 0x36 ? "Right Shift" : "Left Shift",
            0x11 => extended ? "Right Ctrl" : "Left Ctrl",
            0x12 => extended ? "Right Alt" : "Left Alt",
            0x20 => "Space",
            0x0D => extended ? "Keypad Enter" : "Return",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x14 => "CapsLock",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x2D => "Insert",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => null
        };
    }
}
