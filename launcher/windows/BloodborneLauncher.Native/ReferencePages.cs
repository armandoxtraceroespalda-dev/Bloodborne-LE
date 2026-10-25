using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    int _moreTab;
    string _trophyFilter = "all";
    readonly record struct PageButton(string Caption, string Key, Action Click, string Icon = "");
    float WrappedHeight(string text, float font, float width, bool translate = true, bool detail = false) => _canvas.WrappedHeight(translate ? UiText.T(text) : text, font * _scale, Math.Max(1, width), detail);
    void Wrapped(string text, Rect bounds, float font, Color4 color, bool translate = true, bool detail = false) => Add(new(Ink.Text, bounds, color, translate ? UiText.T(text) : text, font * _scale, Detail: detail, Wrap: true));
    void PageGroup(string title, float x, float width, ref float y)
    {
        SectionHeading(title, x, width, y);
        y += 50 * _scale;
    }

    void SectionHeading(string title, float x, float width, float y, string existingGlyph = "")
    {
        string glyph = SectionIcons.For(_page, title, existingGlyph);
        float offset = glyph.Length == 0 ? 0 : 32 * _scale;
        if (glyph.Length > 0)
            Symbol(glyph, new Rect(x + 12 * _scale, y + 8 * _scale, 26 * _scale, 30 * _scale), Gold, 20);
        TextAt(title, new Rect(x + 14 * _scale + offset, y + 8 * _scale, Math.Max(1, width - 28 * _scale - offset), 30 * _scale),
            21.875f, color: new Color4(215 / 255f, 216 / 255f, 213 / 255f, 1));
    }

    void Symbol(string glyph, Rect bounds, Color4? color = null, float size = 20) => Add(new(Ink.Text, bounds, color ?? Gold, glyph, size * _scale, Center: true, Symbol: true));
    void ReferenceCard(string title, string hint, string glyph, float x, float width, ref float y, IReadOnlyList<PageButton> buttons, bool literalHint = false)
    {
        // Game keeps its explanatory text visible, exactly as in the WPF cards. Narrow windows
        // stack the action row instead of hiding the legend or truncating a localized caption.
        float pad = 10 * _scale, icon = glyph.Length > 0 ? 30 * _scale : 0;
        float available = width - 2 * pad - icon;
        float natural = buttons.Sum(b => Math.Max(34 * _scale, _canvas.Measure(UiText.T(b.Caption), 18.375f * _scale).X + (b.Icon.Length > 0 ? 42 : 24) * _scale) + 6 * _scale);
        bool stack = available - Math.Min(natural, available) < 250 * _scale;
        float actionWidth = Math.Min(natural, stack ? available : available * .52f);
        float textWidth = stack ? available : available - actionWidth - 18 * _scale;
        float th = WrappedHeight(title, 19.25f, textWidth);
        bool showHint = literalHint || _page == 2 || title == "Hunter's Dream account";
        float hh = hint.Length == 0 || !showHint ? 0 : WrappedHeight(hint, 14, textWidth, !literalHint, true);
        float textHeight = th + (hh > 0 ? hh + 5 * _scale : 0);
        float top = y + 7 * _scale;
        if (icon > 0)
            Symbol(glyph, new Rect(x + pad, top, 24 * _scale, 32 * _scale));
        Wrapped(title, new Rect(x + pad + icon, top, textWidth, th), 19.25f, Text);
        if (hh > 0)
            Wrapped(hint, new Rect(x + pad + icon, top + th + 5 * _scale, textWidth, hh), 14, Muted, !literalHint, true);
        if (!showHint && hint.Length > 0)
            HitAt(new Rect(x + pad, top, textWidth + icon, Math.Max(38 * _scale, textHeight)), "help-" + title, _ =>
            {
            }, hint);
        float bx = stack ? x + pad + icon : x + width - pad - actionWidth;
        float by = stack ? top + textHeight + 8 * _scale : top + Math.Max(0, (textHeight - 38 * _scale) / 2);
        float origin = bx, right = x + width - pad;
        foreach (var b in buttons)
        {
            float w = Math.Min(right - origin, Math.Max(34 * _scale, _canvas.Measure(UiText.T(b.Caption), 18.375f * _scale).X + (b.Icon.Length > 0 ? 42 : 24) * _scale));
            if (bx > origin && bx + w > right)
            {
                bx = origin;
                by += 42 * _scale;
            }

            Button(b.Caption, new Rect(bx, by, w, 38 * _scale), b.Key, () => Run(b.Click), icon: b.Icon);
            bx += w + 6 * _scale;
        }

        y = Math.Max(top + textHeight, buttons.Count > 0 ? by + 38 * _scale : top + textHeight) + 23 * _scale;
        Line(x + pad, y - 12 * _scale, width - 2 * pad, new Color4(85 / 255f, 89 / 255f, 87 / 255f, .65f));
    }

    void FolderCard(string title, string key, string value, float x, float width, ref float y, bool reset = false)
    {
        var actions = new List<PageButton>
        {
            new("Browse", key + "-browse", () =>
            {
                var selected = NativeDialogs.Folder(_window, UiText.T(title));
                if (selected != null)
                {
                    _profile.Set(key, selected, true);
                    _trophies = null;
                    Rebuild();
                }
            }, "\uE8B7"),
            new("", key + "-open", () => OpenPath(value, true), "\uE8A7")
        };
        if (reset)
            actions.Add(new("", key + "-reset", () =>
            {
                _profile.Set(key, "", true);
                Rebuild();
            }, "\uE7A7"));
        ReferenceCard(title, value.Length > 0 ? value : UiText.T("Not selected"), "\uE8B7", x, width, ref y, actions, true);
    }

    void DrawReferenceGame(float x, float width, ref float y)
    {
        PageGroup("Language", x, width, ref y);
        var languages = PageCatalog.Build(2, _profile).Where(o => o.Section == "Language").ToArray();
        bool two = width > 560 * _scale;
        float lw = two ? (width - 16 * _scale) / 2 : width, start = y;
        for (int i = 0; i < languages.Length; i++)
        {
            var o = languages[i];
            float lx = x + (two ? i * (lw + 16 * _scale) : 0), ly = two ? start : y;
            TextAt(o.Title, new Rect(lx, ly, lw, 32 * _scale), 19.25f, true);
            var box = new Rect(lx + 10 * _scale, ly + 36 * _scale, lw - 20 * _scale, 38 * _scale);
            int hit = _hits.Count;
            ControlFrame(box, hit);
            TextAt(o.Choices!.FirstOrDefault(c => c.Value == _profile.Value(o))?.Label ?? _profile.Value(o), new Rect(box.X + 20 * _scale, box.Y, box.Width - 40 * _scale, box.Height), 18.375f, true, translate: false);
            TextAt("⌄", new Rect(box.Right - 24 * _scale, box.Y, 20 * _scale, box.Height), 18.375f, true, Gold);
            HitAt(box, o.Key, pos => Change(o, pos, ScreenRect(box, true)), _profile.Help.GetValueOrDefault(o.Key, ""), o);
            if (!two)
                y += 92 * _scale;
        }

        if (two)
            y = start + 92 * _scale;
        var s = _profile.ReadSettings();
        FolderCard("Game folder", "game_dir", GameEnvironment.GameDir(s), x, width, ref y);
        var dlc = new Option("", "The Old Hunters (DLC)", "dlc_enabled", OptionKind.Toggle, true, Default: "true", Hint: "Off: the game starts without its DLC. The Game of the Year edition (CUSA03173) always has it");
        DrawOption(dlc, new Rect(x, y, width, 52 * _scale));
        y += 54 * _scale;
        FolderCard("DLC folder", "dlc_dir", Paths.Expand(s.Str("dlc_dir")), x, width, ref y, true);
        ReferenceCard("Add to Steam", "Bloodborne as a non-Steam game in your Steam library: Steam Input, the overlay and Big Picture work with it", "\uE7FC", x, width, ref y, new[] { new PageButton("Add to Steam", "steam-add", AddReferenceSteam, "\uE710") });
        ReferenceCard("Player profile", "Each profile has its own save files and achievements, so several people can play their own runs on this PC", "\uE77B", x, width, ref y, new[] { new PageButton(PlayerProfiles.Current(s), "player-select", () =>
        {
            var o = new Option("", "Player profile", "player_profile", OptionKind.Choice, true, PlayerProfiles.All(_profile.SourceSettings()).Select(n => Choice.Literal(n, n)).ToArray(), Default: "Main");
            Change(o, 0, new Rect(_mouseX, _mouseY, 260 * _scale, 38 * _scale));
        }, "\uE77B"), new PageButton("New…", "player-new", () => PageAction("player-new"), "\uE710"), new PageButton("", "player-rename", RenameReferencePlayer, "\uE8AC"), new PageButton("", "player-remove", RemoveReferencePlayer, "\uE74D") });
        FolderCard("Saves folder", "user_dir", GameEnvironment.SavesRoot(s), x, width, ref y, true);
        ReferenceCard("Save backups", UiText.F($"Before every start, when this profile's saves changed. The newest {SaveBackups.Keep} are kept"), "\uE895", x, width, ref y, new[] { new PageButton("Restore…", "backup-restore", () => PageAction("restore-backup"), "\uE81C"), new PageButton("", "backup-folder", () => PageAction("backups-folder"), "\uE8A7") });
        var backup = new Option("", "Back up saves before playing", "save_backups", OptionKind.Toggle, true, Default: "true");
        DrawOption(backup, new Rect(x, y, width, 52 * _scale));
        y += 54 * _scale;
        ReferenceCard("Import saves", "From shadPS4, another Bloodborne port, or a PS4 save exported decrypted (Apollo Save Tool, Save Wizard). This profile's saves are backed up first", "\uE8B5", x, width, ref y, new[] { new PageButton("Import…", "save-import", ImportReferenceSaves, "\uE896") });
    }

    void AddReferenceSteam()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Bloodborne Launcher.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException(exe);
        if (Confirm(UiText.T("Add to Steam")))
        {
            SteamShortcut.Add(exe, "--play");
            _notice = UiText.T("Added to Steam");
        }
    }

    void RenameReferencePlayer()
    {
        var s = _profile.ReadSettings();
        string old = PlayerProfiles.Current(s);
        if (PlayerProfiles.IsMain(old))
            return;
        Edit("Rename this profile", old, text => _profile.EditSource((settings, ini) =>
        {
            var name = PlayerProfiles.Validate(settings, text, out string error, old);
            if (name == null)
                throw new InvalidOperationException(error);
            PlayerProfiles.Rename(settings, old, name);
        }));
    }

    void RemoveReferencePlayer()
    {
        string name = PlayerProfiles.Current(_profile.SourceSettings());
        if (!PlayerProfiles.IsMain(name) && Confirm(UiText.T("Remove this profile from the list (its saves stay on disk)")))
            _profile.EditSource((s, ini) => PlayerProfiles.Remove(s, name));
        Rebuild();
    }

    void ImportReferenceSaves()
    {
        string? root = NativeDialogs.Folder(_window, UiText.T("Import saves"));
        if (root == null)
            return;
        Background(() => Task.Run(() =>
        {
            var sources = SaveImport.Find(root, root);
            return (Action)(() =>
            {
                if (sources.Count == 0)
                    throw new IOException(UiText.T("That folder has no Bloodborne save files (userdata)."));
                var choices = sources.Select((item, i) => Choice.Literal(item.Dir, i.ToString())).ToArray();
                _importSources = sources;
                _popup = new(new Option("", "Import saves", "native-save-source", OptionKind.Choice, Choices: choices), new Rect(_mouseX, _mouseY, 460 * _scale, 38 * _scale));
            });
        }));
    }

    List<SaveImport.Source>? _importSources;
    void DrawReferenceOnline(float x, float width, ref float y)
    {
        if (HuntersDream.OnlinePaused)
        {
            const string notice = "Online play is paused in this version. The game starts offline whatever is set here; your settings are kept for when it returns.";
            float height = WrappedHeight(notice, 15.75f, width);
            Wrapped(notice, new Rect(x, y, width, height), 15.75f, Muted);
            y += height + 16 * _scale;
        }

        Decorated(new Option("", "Play online", "online_enabled", OptionKind.Toggle, Hint: "Off: the game stays offline") { Heading = true }, "\uE774", x, width, ref y);
        Decorated(new Option("", "Server", "online_pool", OptionKind.Choice, Choices: Choices.OnlineServers, Default: "huntersdream", Hint: "Messages, bloodstains, ghosts, co-op and invasions") { Heading = true }, "\uE968", x, width, ref y);
        ReferenceCard("The Hunter's Dream", "Players online, the map and your hunter's profile", "\uE774", x, width, ref y, new[] { new PageButton("Open website", "online-website", () => OpenPath(LauncherSettings.OnlineCommunity), "\uE8A7") });
        PageGroup("The Hunter's Dream account", x, width, ref y);
        bool linked = _profile.ReadSettings().HuntersDreamToken.Length > 0;
        ReferenceCard("Hunter's Dream account", linked ? UiText.F($"Connected as {_profile.Str("hd_name")}") : "Not connected. Connect with Discord, or create an account here without it", "\uE77B", x, width, ref y, new[] { new PageButton(linked ? "Disconnect" : "Connect", "online-account", ShowReferenceAccount, "\uE77B") });
        PageGroup("shadNet account", x, width, ref y);
        float hh = WrappedHeight("Enter the account name you registered (up to 16 characters), not an email address. The password is kept encrypted for your Windows user, not in bbport.ini.", 15.75f, width - 20 * _scale);
        Wrapped("Enter the account name you registered (up to 16 characters), not an email address. The password is kept encrypted for your Windows user, not in bbport.ini.", new Rect(x + 10 * _scale, y, width - 20 * _scale, hh), 15.75f, Muted);
        y += hh + 16 * _scale;
        ReferenceCard("No account yet?", "Register one, then enter it below", "\uE8FA", x, width, ref y, new[] { new PageButton("Create account", "shadnet-register", () => OpenPath("https://shadnet.shadps4.net"), "\uE8A7") });
        Decorated(new Option("", "Account name", "online_username", OptionKind.Text, Default: ""), "\uE77B", x, width, ref y);
        Decorated(new Option("", "Password", "online_password", OptionKind.Password, true, Default: ""), "\uE72E", x, width, ref y);
        PageGroup("Connection", x, width, ref y);
        var rows = PageCatalog.Build(5, _profile).Where(o => o.Section == "Connection").ToArray();
        DrawReferenceRows(rows.Where(o => o.Key != "online_sign_timeout" && o.Kind != OptionKind.Action).ToArray(), x, width, ref y, true);
        DrawReferenceRows(rows.Where(o => o.Key == "online_sign_timeout" || o.Kind == OptionKind.Action).ToArray(), x, width, ref y, false);
    }

    void ShowReferenceAccount()
    {
        if (_profile.SourceSettings().HuntersDreamToken.Length > 0)
        {
            LinkAccount();
            return;
        }

        var choices = new[]
        {
            new Choice("Discord", "discord"),
            new Choice("Without Discord…", "local"),
            new Choice("Recover with a code…", "recover")
        };
        _popupOffset = 0;
        _popup = new(new Option("", "The Hunter's Dream account", "native-account-method", OptionKind.Choice, Choices: choices), new Rect(_mouseX, _mouseY, 340 * _scale, 38 * _scale));
        Rebuild();
    }

    void Decorated(Option option, string glyph, float x, float width, ref float y)
    {
        Symbol(glyph, new Rect(x + 10 * _scale, y + 8 * _scale, 24 * _scale, 32 * _scale));
        float height = OptionHeight(option, width - 32 * _scale);
        DrawOption(option, new Rect(x + 32 * _scale, y, width - 32 * _scale, height));
        y += height + 2 * _scale;
    }

    void DrawReferenceSettings(float x, float width, ref float y)
    {
        FolderCard("Engine folder", "port_dir", _profile.Str("port_dir", Paths.EngineDir), x, width, ref y, true);
        var rows = PageCatalog.Build(7, _profile).Where(o => !o.Key.StartsWith("action:settings-", StringComparison.Ordinal) && (o.Key is not ("custom_command" or "custom_args") || _profile.Str("launch_method") == "custom"));
        DrawLaunchAndAbout(rows.ToArray(), x, width, ref y);
        PageGroup("Files", x, width, ref y);
        // Source Files cards retain their individual icons and paired action buttons.
        float column = width >= 820 ? (width - 24 * _scale) / 2 : width;
        float a = y, b = y;
        ReferenceCard("Launcher settings", _profile.ConfigPath, "\uE713", x, column, ref a, new[] { new PageButton("", "files-settings", () => OpenPath(Path.GetDirectoryName(_profile.ConfigPath)!, true), "\uE8A7") }, true);
        if (column == width)
            b = a;
        ReferenceCard("Port settings (bbport.ini)", _profile.OriginalIni, "\uE713", column == width ? x : x + column + 24 * _scale, column, ref b, new[] { new PageButton("", "files-ini", () => OpenPath(Path.GetDirectoryName(_profile.OriginalIni)!, true), "\uE8A7") }, true);
        y = Math.Max(a, b);
        ReferenceCard("Shader cache", "The game's compiled shaders, kept between starts (a new engine version clears them itself). Clear them if something draws wrong in every save, such as missing skin or hair; the next start compiles them again and stutters for a while", "\uE894", x, width, ref y, new[] { new PageButton("Clear", "cache-clear", ClearReferenceCache, "\uE894") });
        ReferenceCard("Share your settings", "Graphics, effects, camera, controls, cheats and engine options, as a code to paste in Discord or a file. Not included: display and HDR, the online account, folders and languages", "\uE72D", x, width, ref y, new[] { new PageButton("Copy code", "share-copy", () => NativeDialogs.Copy(_window, SharedSettings.ToCode(SharedSettings.Take(_profile.Ini, _profile.SourceSettings()))), "\uE8C8"), new PageButton("Save as file", "share-file", () => PageAction("settings-share"), "\uE74E") });
        ReferenceCard("Import settings", "Someone's code or file. You see what changes before anything is applied", "\uE896", x, width, ref y, new[] { new PageButton("Paste code", "import-code", () => Edit("Import settings", NativeDialogs.Paste(_window), text =>
        {
            if (Confirm(UiText.T("Import settings")))
                _profile.ImportSettings(text);
        }), "\uE77F"), new PageButton("Open file", "import-file", () => PageAction("settings-import"), "\uE8B7") });
    }

    void ClearReferenceCache()
    {
        if (System.Diagnostics.Process.GetProcessesByName("bb-probe").Length > 0)
            throw new InvalidOperationException(UiText.T("Close the game first: it is using the cache."));
        string cache = Path.Combine(Paths.PortDir, "user", "cache");
        if (!Directory.Exists(cache) || !Confirm(UiText.T("Clear shader cache")))
            return;
        foreach (var entry in new DirectoryInfo(cache).EnumerateFileSystemInfos())
        {
            if (entry.Name == "engine.txt")
                continue;
            if (entry is DirectoryInfo directory)
                directory.Delete(true);
            else
                entry.Delete();
        }

        Rebuild();
    }

    void DrawReferenceMore(float x, float width, ref float y)
    {
        float tabWidth = Math.Min(140 * _scale, width / 3), start = x + (width - tabWidth * 3) / 2;
        // MorePage's segmented header stays above the scrolling child page.
        _scrollContent = false;
        _clip = null;
        string[] titles =
        {
            "Mods",
            "Log",
            "Engine"
        };
        for (int i = 0; i < 3; i++)
        {
            int tab = i;
            Button(titles[i], new Rect(start + i * tabWidth, y, tabWidth, 38 * _scale), "more-tab-" + i, () => OpenReferenceMore(tab), _moreTab == i);
        }

        y += 54 * _scale;
        _viewport = new Rect(_viewport.X, y, _viewport.Width, Math.Max(1, _height - y - 8));
        _clip = _viewport;
        _scrollContent = true;
        if (_moreTab == 2)
        {
            if (_subpage != "Engine")
                ShowEngine();
            DrawReferenceSections(_pageRows ?? Array.Empty<Option>(), x, width, ref y);
        }
        else if (_moreTab == 1)
            DrawReferenceOutput(x, width, ref y);
        else
            DrawReferenceMods(x, width, ref y);
    }

    void OpenReferenceMore(int tab)
    {
        _moreTab = tab;
        _scroll = 0;
        _pageRows = null;
        _logLines = null;
        _subpage = "";
        if (tab == 0)
            RefreshReferenceMods();
        else if (tab == 2)
            ShowEngine();
        else
        {
            _nextOutputPoll = 0;
            _outputFollowPending = _followOutput;
            PollReferenceOutput(_clock.Elapsed.TotalSeconds);
        }

        Rebuild();
    }

    void DrawReferenceAchievements(float x, float width, ref float y)
    {
        if (_trophies == null)
        {
            _trophies = new();
            _trophies.SetUserDir(GameEnvironment.UserDir(_profile.ReadSettings()));
        }

        _trophies.Poll(false);
        var tracker = _trophies;
        TextAt($"{tracker.Completion * 100:0}%", new Rect(x + 12 * _scale, y, 110 * _scale, 62 * _scale), 35, color: Gold, translate: false);
        TextAt("COMPLETION", new Rect(x + 140 * _scale, y, width - 150 * _scale, 22 * _scale), 12, color: Muted);
        TextAt(UiText.F($"{tracker.Count} of {TrophyTracker.All.Length} achievements"), new Rect(x + 140 * _scale, y + 22 * _scale, width - 150 * _scale, 28 * _scale), 19.25f, translate: false);
        TextAt(UiText.F($"{tracker.EarnedPoints} of {tracker.TotalPoints} points"), new Rect(x + 140 * _scale, y + 50 * _scale, width - 150 * _scale, 25 * _scale), 14, color: Muted, translate: false);
        y += 88 * _scale;
        Progress(new Rect(x + 12 * _scale, y, width - 24 * _scale, 10 * _scale), tracker.Completion);
        y += 30 * _scale;
        float half = (width - 24 * _scale) / 2;
        for (int i = 0; i < 2; i++)
        {
            var p = tracker.Progress(i == 1);
            float px = x + i * (half + 24 * _scale);
            TextAt(i == 0 ? "Bloodborne" : "The Old Hunters", new Rect(px, y, half - 80 * _scale, 26 * _scale), 16, color: Muted);
            TextAt($"{p.Earned} / {p.Total}", new Rect(px + half - 80 * _scale, y, 80 * _scale, 26 * _scale), 16, true, translate: false);
            Progress(new Rect(px, y + 32 * _scale, half, 5 * _scale), p.Total == 0 ? 0 : (double)p.Earned / p.Total);
        }

        y += 56 * _scale;
        var grades = new[]
        {
            Grade.Platinum,
            Grade.Gold,
            Grade.Silver,
            Grade.Bronze
        };
        for (int i = 0; i < grades.Length; i++)
        {
            var grade = grades[i];
            var records = TrophyTracker.All.Where(t => t.Grade == grade).ToArray();
            float gx = x + i * width / 4;
            TextAt(grade.ToString(), new Rect(gx, y, width / 4, 22 * _scale), 12, color: Muted);
            Symbol("\uE735", new Rect(gx, y + 26 * _scale, 22 * _scale, 28 * _scale), GradeColor(grade));
            TextAt($"{records.Count(t => tracker.Unlocked.ContainsKey(t.Id))} / {records.Length}", new Rect(gx + 32 * _scale, y + 26 * _scale, width / 4 - 36 * _scale, 28 * _scale), 19.25f, translate: false);
        }

        y += 86 * _scale;
        TextAt("All achievements", new Rect(x, y, width - 165 * _scale, 38 * _scale), 21.875f);
        var filter = new Option("", "All achievements", "native-trophy-filter", OptionKind.Choice, Choices: new[] { new Choice("All", "all"), new Choice("Earned", "earned"), new Choice("Locked", "locked") }, Default: _trophyFilter);
        var fb = new Rect(x + width - 150 * _scale, y, 150 * _scale, 38 * _scale);
        int idx = _hits.Count;
        ControlFrame(fb, idx);
        TextAt(filter.Choices!.First(c => c.Value == _trophyFilter).Label, fb, 18.375f, true, translate: false);
        HitAt(fb, filter.Key, p => Change(filter, p, ScreenRect(fb, true)), option: filter);
        y += 52 * _scale;
        var settings = _profile.ReadSettings();
        int count = 0;
        foreach (var trophy in TrophyTracker.All)
        {
            bool earned = tracker.Unlocked.TryGetValue(trophy.Id, out long when);
            if (_trophyFilter == "earned" && !earned || _trophyFilter == "locked" && earned)
                continue;
            count++;
            string description = trophy.Description + (trophy.Dlc ? "  ·  The Old Hunters" : "");
            float tw = Math.Max(120 * _scale, width - 210 * _scale);
            float titleHeight = WrappedHeight(trophy.Name, 19.25f, tw), descHeight = WrappedHeight(description, 15.75f, tw);
            float h = Math.Max(60 * _scale, titleHeight + descHeight + 10 * _scale);
            string? image = TrophyIcons.For(settings, trophy.Id);
            if (image != null)
                Add(new(Ink.Image, new Rect(x + 10 * _scale, y + (h - 36 * _scale) / 2, 36 * _scale, 36 * _scale), new Color4(1, 1, 1, earned ? 1 : .62f), Image: image + (earned ? "" : "|gray")));
            else
                Symbol("\uE735", new Rect(x + 10 * _scale, y, 36 * _scale, h), earned ? GradeColor(trophy.Grade) : new Color4(.7f, .7f, .7f, .62f), 26);
            Wrapped(trophy.Name, new Rect(x + 60 * _scale, y, tw, titleHeight), 19.25f, new Color4(.89f, .89f, .87f, earned ? 1 : .46f));
            Wrapped(description, new Rect(x + 60 * _scale, y + titleHeight + 5 * _scale, tw, descHeight), 15.75f, new Color4(.82f, .82f, .80f, earned ? .9f : .38f));
            string status = earned ? DateTimeOffset.FromUnixTimeSeconds(when).LocalDateTime.ToString("d MMM yyyy, HH:mm") : UiText.T("Locked");
            TextAt(status, new Rect(x + width - 140 * _scale, y, 135 * _scale, h), 13, true, new Color4(.8f, .8f, .8f, earned ? .85f : .38f), translate: false);
            y += h + 22 * _scale;
            Line(x + 10 * _scale, y - 12 * _scale, width - 20 * _scale, new Color4(.33f, .35f, .34f, .65f));
        }

        if (count == 0)
        {
            TextAt(_trophyFilter == "earned" ? "No achievements earned yet." : "Every achievement is unlocked. Hats off!", new Rect(x, y, width, 38 * _scale), 16, color: Muted);
            y += 52 * _scale;
        }

        DrawAchievementNotifications(tracker, settings, x, width, ref y);
    }

    void ChooseTrophySound()
    {
        string? file = NativeDialogs.File(_window, false, UiText.T("Achievement sound"), "Sound files\0*.wav;*.mp3;*.mp4;*.m4a;*.aac;*.wma\0All files\0*.*\0\0", "wav");
        if (file != null)
        {
            _profile.Set("trophy_sound_file", file, true);
            Rebuild();
        }
    }

    void Progress(Rect box, double value)
    {
        Fill(box, new Color4(.16f, .17f, .18f, 1));
        Fill(new Rect(box.X, box.Y, box.Width * (float)Math.Clamp(value, 0, 1), box.Height), Gold);
    }

    static Color4 GradeColor(Grade grade) => grade switch
    {
        Grade.Platinum => new(.67f, .84f, .94f, 1),
        Grade.Gold => new(.85f, .70f, .36f, 1),
        Grade.Silver => new(.73f, .76f, .8f, 1),
        _ => new(.68f, .43f, .27f, 1)};
}
