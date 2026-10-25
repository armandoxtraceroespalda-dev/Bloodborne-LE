using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BloodborneLauncher.Build;
using BloodborneLauncher.Core;
using BloodborneLauncher.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;

namespace BloodborneLauncher.Views
{
    /// <summary>The launcher itself: where the port is, how the game is started, and rebuilding.</summary>
    public sealed class SettingsPage : LauncherPage
    {
        private readonly TextBlock _portHint;
#if false // Intentionally disabled; retained as upstream reference.
        private readonly TextBlock _buildHint;
#endif
        private readonly StackPanel _customRows = new OptionRows();
        private readonly StackPanel _iconPicture = new OptionRows();
        private TextBlock _iconPictureHint = null!;
        private void RefreshIcon()
        {
            _iconPicture.Visibility = S.Str("app_icon") == "custom" ? Visibility.Visible : Visibility.Collapsed;
            SetHint(_iconPictureHint, File.Exists(AppIcon.CustomFile) ? S.Str("app_icon_image") : UiText.T("None chosen yet: the build's own icon shows"));
            AppIcon.Apply(MainWindow.Instance, S.Str("app_icon"));
            AppIcon.SyncShortcuts(Paths.PortDir, S.Str("app_icon"));
        }
        private void ChooseIcon()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog {
                Title=UiText.T("Icon picture"),
                Filter=UiText.T("Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All files|*.*") };
            if(dialog.ShowDialog()!=true)return;
            if(!AppIcon.MakeCustom(dialog.FileName)) {
                Prompt.Tell(UiText.T("Icon picture"),UiText.F($"{Path.GetFileName(dialog.FileName)} could not be read as a picture."));return;
            }
            S.Set("app_icon_image",dialog.FileName);Ws.SaveSoon();RefreshIcon();
        }
// Upstream release selection is disabled in this customized launcher.
#if false // Intentionally disabled; retained as upstream reference.
        private readonly TextBlock _updateHint;
        private readonly BloodborneLauncher.Controls.Button _updateNow;
        private readonly ComboBox _versions = new() { Width = 300, VerticalAlignment = VerticalAlignment.Center };
        private readonly BloodborneLauncher.Controls.Button _installVersion;
        private readonly TextBlock _versionHint;
        private IReadOnlyList<Updater.Release>? _releases;
        private bool _versionsLoading;

#endif

        public SettingsPage() : base(BloodborneLauncher.Core.UiText.T("Settings"), BloodborneLauncher.Core.UiText.T("The Bloodborne PC engine, launch settings and diagnostics."))
        {
            Group(UiText.T("About"));
            Body.Children.Add(Text("Lantern Engine by Awaken (https://github.com/Awakenchan/LanternEngine)\n" +
                "Modified BetterKBM edition. GNU GPL v3 or later with additional terms; see NOTICE.", "Faint"));
            Group(BloodborneLauncher.Core.UiText.T("Bloodborne PC"));
            Add(Card(SymbolRegular.FolderLink24, BloodborneLauncher.Core.UiText.T("Engine folder"), "", Row(
                SmallButton(BloodborneLauncher.Core.UiText.T("Browse"), SymbolRegular.FolderOpen24, (_, _) => ChoosePort()),
                IconButton(SymbolRegular.Open24, BloodborneLauncher.Core.UiText.T("Open in Explorer"), (_, _) => Paths.OpenFolder(Paths.PortDir)),
                IconButton(SymbolRegular.ArrowUndo24, BloodborneLauncher.Core.UiText.T("Detect automatically"), (_, _) =>
                {
                    S.Set("port_dir", "");
                    Ws.ResolvePort();
                    Ws.SaveSoon();
                    Refresh();
                })), out _portHint));

            Group(UiText.T("Appearance"));
            SettingCombo(SymbolRegular.Image24, UiText.T("Launcher icon"),
                UiText.T("The launcher window's icon. The game keeps its extracted Bloodborne icon."),
                AppIcon.Choices,"app_icon",changed:RefreshIcon);
            Add(Card(SymbolRegular.Image24,UiText.T("Icon picture"),UiText.T("A PNG, JPG, BMP or ICO; square pictures look best"),
                SmallButton(UiText.T("Choose"),SymbolRegular.FolderOpen24,(_,_)=>ChooseIcon()),out _iconPictureHint),_iconPicture);
            Body.Children.Add(_iconPicture);RefreshIcon();

            Group(BloodborneLauncher.Core.UiText.T("Launch"));
            SettingCombo(SymbolRegular.Rocket24, BloodborneLauncher.Core.UiText.T("Start the game with"), null, Choices.LaunchMethods, "launch_method", changed: () => { ShowMethod(); Ws.SaveSoon(); });

            TextRow(SymbolRegular.Code24, BloodborneLauncher.Core.UiText.T("Command"), BloodborneLauncher.Core.UiText.T("A program to run instead of Bloodborne.exe --run, with the settings as environment variables"),
                @"C:\path\to\game.exe", () => S.Str("custom_command"), v => S.Set("custom_command", v), _customRows, 380);
            TextRow(SymbolRegular.TextDescription24, BloodborneLauncher.Core.UiText.T("Arguments"), null, BloodborneLauncher.Core.UiText.T("Optional"),
                () => S.Str("custom_args"), v => S.Set("custom_args", v), _customRows, 380);
            Body.Children.Add(_customRows);

            Group(BloodborneLauncher.Core.UiText.T("Performance"));
            SettingCombo(SymbolRegular.Flow20, BloodborneLauncher.Core.UiText.T("Two-stage GPU pipeline"), BloodborneLauncher.Core.UiText.T("20–30% faster; turn off if the game is unstable"),
                Choices.DrawPipe, "draw_pipe");
            SettingCombo(SymbolRegular.ArrowSwap24, BloodborneLauncher.Core.UiText.T("CPU reads of GPU data"), BloodborneLauncher.Core.UiText.T("Relaxed is the default"), Choices.Readbacks, "readbacks");
            SettingToggle(SymbolRegular.Speaker224, BloodborneLauncher.Core.UiText.T("Sound threads first"),
                BloodborneLauncher.Core.UiText.T("For CPUs with many slower cores (older Xeons and similar): the game's sound runs before its other threads, against late or stuttering sound. Off by default"),
                "audio_priority");
            SettingCombo(SymbolRegular.Speaker224, BloodborneLauncher.Core.UiText.T("Sound output"),
                BloodborneLauncher.Core.UiText.T("If the sound jitters or crackles, try DirectSound. The larger buffer is steadier but the sound comes a little later"),
                Choices.SoundOutput, "sound_output");

            SettingsColumns.SetTwoColumns(Group(BloodborneLauncher.Core.UiText.T("Diagnostics")), true);
            SettingToggle(SymbolRegular.TextBulletListSquare24, BloodborneLauncher.Core.UiText.T("Frame statistics in the log"), "BB_FRAME_STATS", "frame_stats");
            SettingToggle(SymbolRegular.DeveloperBoard24, BloodborneLauncher.Core.UiText.T("FSR 4 profile in the log"), "BB_FSR4_PROFILE", "fsr_profile");
            SettingToggle(SymbolRegular.Speaker224, BloodborneLauncher.Core.UiText.T("Audio statistics in the log"), "BB_AUDIO_STATS", "audio_stats");
            SettingToggle(SymbolRegular.HourglassHalf24, BloodborneLauncher.Core.UiText.T("Loading profile in the log"),
                "BB_SAMPLE_THREADS: where the game's main, render and file threads spend their time, every 5 seconds. For slow loading; costs a little speed", "load_profile");
            SettingToggle(SymbolRegular.Timer24, BloodborneLauncher.Core.UiText.T("Render stage timings in the log"), "BB_SECTIONS: where each frame's CPU time goes (with frame statistics). It costs FPS, so it applies to the next game only", "section_stats");
            TextRow(SymbolRegular.Code24, BloodborneLauncher.Core.UiText.T("Extra variables"), BloodborneLauncher.Core.UiText.T("NAME=value pairs separated by spaces, passed to the game"), "BB_EXAMPLE=1",
                () => S.Str("extra_env"), v => S.Set("extra_env", v.Trim()), width: 300);

            SettingsColumns.SetTwoColumns(Group(UiText.T("Files")), true);
            Add(Card(SymbolRegular.DocumentSettings20, BloodborneLauncher.Core.UiText.T("Launcher settings"), BloodborneLauncher.Core.UiText.T(Paths.ConfigFile),
                IconButton(SymbolRegular.Open24, BloodborneLauncher.Core.UiText.T("Open in Explorer"), (_, _) => Paths.OpenFolder(Paths.ConfigDir)), out _));
            Add(Card(SymbolRegular.DocumentSettings20, BloodborneLauncher.Core.UiText.T("Port settings (bbport.ini)"), BloodborneLauncher.Core.UiText.T(Paths.IniPath),
                IconButton(SymbolRegular.Open24, BloodborneLauncher.Core.UiText.T("Open in Explorer"), (_, _) => Paths.OpenFolder(Path.GetDirectoryName(Paths.IniPath)!)), out _));
            Add(Card(SymbolRegular.Broom24, BloodborneLauncher.Core.UiText.T("Shader cache"),
                BloodborneLauncher.Core.UiText.Format("The game\u0027s compiled shaders, kept between starts (a new engine version clears them itself). Clear them if something draws wrong in every save, such as missing skin or hair; the next start compiles them again and stutters for a while"),
                SmallButton(BloodborneLauncher.Core.UiText.T("Clear"), SymbolRegular.Broom24, (_, _) => ClearShaderCache()), out _));

            Add(Card(SymbolRegular.Share24, BloodborneLauncher.Core.UiText.T("Share your settings"),
                BloodborneLauncher.Core.UiText.Format("Graphics, effects, camera, controls, cheats and engine options, as a code to paste in Discord or a file. Not included: display and HDR, the online account, folders and languages"),
                Row(SmallButton(BloodborneLauncher.Core.UiText.T("Copy code"), SymbolRegular.Copy24, (_, _) => CopySettingsCode()),
                    SmallButton(BloodborneLauncher.Core.UiText.T("Save as file"), SymbolRegular.Save24, (_, _) => SaveSettingsFile())), out _, wide: true));
            Add(Card(SymbolRegular.ArrowImport24, BloodborneLauncher.Core.UiText.T("Import settings"), BloodborneLauncher.Core.UiText.T("Someone's code or file. You see what changes before anything is applied"),
                Row(SmallButton(BloodborneLauncher.Core.UiText.T("Paste code"), SymbolRegular.ClipboardPaste24, (_, _) => ImportSettingsCode()),
                    SmallButton(BloodborneLauncher.Core.UiText.T("Open file"), SymbolRegular.FolderOpen24, (_, _) => ImportSettingsFile())), out _, wide: true));

// No community/Discord section is presented in this launcher.
#if false // Intentionally disabled; retained as upstream reference.
            Group("Community");
            Add(Card(SymbolRegular.PeopleCommunity24, "Discord", "Help, news, co-op partners and beta feedback",
                SmallButton("Join", SymbolRegular.Open24, (_, _) =>
                    Process.Start(new ProcessStartInfo(LauncherSettings.Discord) { UseShellExecute = true })), out _));

#endif

// Do not construct update controls, subscriptions or release requests.
#if false // Intentionally disabled; retained as upstream reference.
            Group("Updates");
            _updateNow = SmallButton("Update now", SymbolRegular.ArrowDownload24, async (_, _) => await MainWindow.Instance!.InstallUpdateAsync(),
                ControlAppearance.Primary);
            Add(Card(SymbolRegular.ArrowDownload24, "Launcher updates", "", Row(
                SmallButton("Check now", SymbolRegular.ArrowClockwise24, async (_, _) => await Ws.CheckForUpdatesAsync(quiet: false)),
                _updateNow,
                IconButton(SymbolRegular.Open24, "Open the releases page", (_, _) =>
                    Process.Start(new ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }))), out _updateHint));
            ComboRow(SymbolRegular.Branch24, "Update channel",
                "Stable: releases that have been tested. Beta: every change as soon as it is done, before it is tested",
                new[] { new Choice("Stable", "stable"), new Choice("Beta", "beta") },
                () => Updater.BetaChannel(S) ? "beta" : "stable",
                v =>
                {
                    if (v == (Updater.BetaChannel(S) ? "beta" : "stable")) return;
                    S.Set("update_channel", v);
                    Ws.SaveSoon();
                    Ws.Log(v == "beta" ? "Update channel: beta." : "Update channel: stable.", LogLevel.Step);
                    _ = Ws.CheckForUpdatesAsync(quiet: false);
                });
            SettingToggle(SymbolRegular.Alert24, "Check for updates at start", $"Looks at github.com/{Updater.Repository}/releases", "check_updates");
            _installVersion = SmallButton("Install", SymbolRegular.ArrowDownload24, async (_, _) => await InstallChosenVersionAsync());
            _installVersion.IsEnabled = false;
            _versions.SelectionChanged += (_, _) =>
                _installVersion.IsEnabled = _versions.SelectedItem is ComboBoxItem { Tag: Updater.Release r } && !r.IsCurrent;
            Add(Card(SymbolRegular.History24, "Version",
                "Every published release, stable and beta, newest first. Install any of them, older ones too, to go back if a " +
                "version gives you trouble. Your settings, saves, mods and profiles stay",
                Row(_versions, _installVersion,
                    IconButton(SymbolRegular.ArrowClockwise24, "Reload the list", async (_, _) => await LoadVersionsAsync(force: true))),
                out _versionHint));
            SetHint(_versionHint, $"You have {Updater.CurrentLabel}");
            Ws.UpdateStateChanged += UpdateUpdates;
            ReleaseWhenDisposed(() => Ws.UpdateStateChanged -= UpdateUpdates);
            UpdateUpdates();

#endif

            // Self-rebuild controls are hidden along with update/version replacement options.
#if false // Intentionally disabled; retained as upstream reference.
            // Only a copy built from the source checkout can rebuild itself.
            var build = Card(SymbolRegular.ArrowSync24, "Build", "", Row(
                SmallButton("Rebuild", SymbolRegular.ArrowClockwise24, (_, _) => Rebuild(force: false)),
                SmallButton("Force rebuild", SymbolRegular.Wrench24, (_, _) => Rebuild(force: true))), out _buildHint);
            if (Paths.SourceRoot != null)
            {
                Group("Build from source");
                Add(build);
            }

#endif

            Ws.PortChanged += Refresh;
            ReleaseWhenDisposed(() => Ws.PortChanged -= Refresh);
        }

        public override void OnShown()
        {
            Refresh();
            ShowMethod();
#if false // Intentionally disabled; retained as upstream reference.
            _ = LoadVersionsAsync(force: false);
#endif
        }

// Preserved upstream version-list implementation; not compiled by default.
#if false // Intentionally disabled; retained as upstream reference.
        /// <summary>Fills the version selector from the releases on GitHub (once, or again on reload).</summary>
        private async Task LoadVersionsAsync(bool force)
        {
            if (_versionsLoading || (_releases != null && !force)) return;
            _versionsLoading = true;
            _versions.IsEnabled = false;
            _installVersion.IsEnabled = false;
            _versions.Items.Clear();
            _versions.Items.Add(new ComboBoxItem { Content = "Loading…" });
            _versions.SelectedIndex = 0;
            try
            {
                _releases = await Updater.AllAsync();
                _versions.Items.Clear();
                ComboBoxItem? current = null;
                foreach (var release in _releases)
                {
                    var label = Updater.Label(release.Version, release.Beta) +
                                (release.Beta > 0 ? "" : "  · stable") +
                                (release.IsCurrent ? "  (installed)" : "");
                    var item = new ComboBoxItem { Content = label, Tag = release };
                    _versions.Items.Add(item);
                    if (release.IsCurrent) current = item;
                }
                _versions.SelectedItem = current ?? (_versions.Items.Count > 0 ? _versions.Items[0] : null);
                _versions.IsEnabled = _versions.Items.Count > 0;
                SetHint(_versionHint, _releases.Count == 0
                    ? $"You have {Updater.CurrentLabel}; no release is published yet"
                    : $"You have {Updater.CurrentLabel}. {_releases.Count} versions to choose from");
            }
            catch (Exception e)
            {
                _releases = null;
                _versions.Items.Clear();
                SetHint(_versionHint, $"You have {Updater.CurrentLabel}. The list could not be loaded: {e.GetBaseException().Message}");
            }
            finally
            {
                _versionsLoading = false;
            }
        }

        private async Task InstallChosenVersionAsync()
        {
            if (_versions.SelectedItem is not ComboBoxItem { Tag: Updater.Release release }) return;
            await MainWindow.Instance!.InstallVersionAsync(release);
        }

#endif

        private void Refresh()
        {
            SetHint(_portHint, Paths.IsPortDir(Paths.PortDir)
                ? Paths.PortDir + (Paths.DetectPortDir() == Paths.PortDir ? "  ·  the package this launcher is in" : "") + "\nThe engine that runs your game: out\\bb-probe.exe, its scripts and Python. Your game files stay where they are"
                : "Not found. It is normally the folder this launcher is in (with bin\\bb-probe.exe and run.py)");
#if false // Intentionally disabled; retained as upstream reference.
            SetHint(_buildHint, BuildStatus());
#endif
        }

// The update state has no UI in this build.
#if false // Intentionally disabled; retained as upstream reference.
        private void UpdateUpdates()
        {
            var update = Ws.Update;
            var latest = Ws.Latest;
            _updateNow.Visibility = update != null ? Visibility.Visible : Visibility.Collapsed;
            SetHint(_updateHint,
                update != null ? $"{update.Name} is available (you have {Updater.CurrentLabel})" +
                                 (update.IsNewerThan(Updater.Current, Updater.CurrentBeta) ? "" : ": back to stable")
                : Ws.CheckedAt == null ? $"You have {Updater.CurrentLabel}"
                : latest == null ? $"You have {Updater.CurrentLabel}; no release is published yet"
                : $"You have {Updater.CurrentLabel}, the latest ({latest.Tag})");
        }

#endif

        private static void Restart()
        {
            Ws.SaveNow();
            if (Environment.ProcessPath is { } self)
                Process.Start(new ProcessStartInfo(self) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(self)! });
            Application.Current.Shutdown();
        }

        /// <summary>Deletes the engine's shader and pipeline caches (user\cache beside the engine;
        /// scripts\run_windows.py also clears them when the engine changes).</summary>
        private static void ClearShaderCache()
        {
            if (Process.GetProcessesByName("bb-probe").Length > 0)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Shader cache"), BloodborneLauncher.Core.UiText.T("Close the game first: it is using the cache."));
                return;
            }
            var cache = Path.Combine(Paths.PortDir, "user", "cache");
            var failed = 0;
            if (Directory.Exists(cache))
            {
                foreach (var entry in new DirectoryInfo(cache).EnumerateFileSystemInfos())
                {
                    if (entry.Name == "engine.txt") continue;
                    try
                    {
                        if (entry is DirectoryInfo directory) directory.Delete(recursive: true);
                        else entry.Delete();
                    }
                    catch (Exception) { failed++; }
                }
            }
            Ws.Log(failed == 0 ? BloodborneLauncher.Core.UiText.T("Shader cache cleared: the next start compiles the shaders again.")
                               : BloodborneLauncher.Core.UiText.F($"Shader cache: {failed} entries could not be removed ({cache})."),
                   failed == 0 ? LogLevel.Good : LogLevel.Warn);
        }

        // ---------------------------------------------------------------- shared settings

        private void CopySettingsCode()
        {
            Clipboard.SetText(SharedSettings.ToCode(SharedSettings.Take(Ini, S)));
            Ws.Log(BloodborneLauncher.Core.UiText.T("Settings code copied. Paste it in Discord; others import it in Settings › Share settings."), LogLevel.Good);
            Prompt.Tell(BloodborneLauncher.Core.UiText.T("Settings code copied"), BloodborneLauncher.Core.UiText.T("Paste it in Discord or anywhere else. Others use Import settings › Paste code."));
        }

        private void SaveSettingsFile()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = BloodborneLauncher.Core.UiText.T("Save settings"), FileName = "Bloodborne PC settings.json", Filter = "Settings|*.json" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, SharedSettings.ToJson(SharedSettings.Take(Ini, S)));
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Settings saved: {0}",dialog.FileName), LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Could not save the settings: {0}",e.Message), LogLevel.Bad);
            }
        }

        private void ImportSettingsCode()
        {
            var clipboard = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            var values = Prompt.Ask(BloodborneLauncher.Core.UiText.T("Import settings"), BloodborneLauncher.Core.UiText.F($"Paste the code someone shared (it starts with {SharedSettings.CodePrefix})."),
                new Prompt.Field(BloodborneLauncher.Core.UiText.T("Code"), clipboard.Contains(SharedSettings.CodePrefix, StringComparison.OrdinalIgnoreCase) ? clipboard.Trim() : "",
                    SharedSettings.CodePrefix + "…", Multiline: true));
            if (values == null || values[0].Trim().Length == 0) return;
            OfferSettings(values[0]);
        }

        private void ImportSettingsFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = BloodborneLauncher.Core.UiText.T("Import settings"), Filter = "Settings|*.json;*.txt|All files|*.*" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 256 * 1024)
                    throw new FormatException(BloodborneLauncher.Core.UiText.T("That file is far too large to be shared settings."));
                OfferSettings(File.ReadAllText(dialog.FileName));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Could not import"), BloodborneLauncher.Core.UiText.T(e.Message));
            }
        }

        /// <summary>Shows what imported settings change and, if accepted, applies them (the previous ones are
        /// kept in a file) and restarts the launcher so every page shows them.</summary>
        private void OfferSettings(string text)
        {
            if (Ws.Game.Running)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Close the game first"), BloodborneLauncher.Core.UiText.T("The game changes these settings too while it runs."));
                return;
            }
            SharedSettings.Snapshot shared;
            try
            {
                shared = SharedSettings.Parse(text);
            }
            catch (FormatException e)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Could not import"), BloodborneLauncher.Core.UiText.T(e.Message));
                return;
            }

            var everything = new HashSet<SharedSettings.Part> { SharedSettings.Part.Settings, SharedSettings.Part.Controls, SharedSettings.Part.Cheats };
            var changes = SharedSettings.Changes(shared, Ini, S, everything);
            if (changes.Count == 0)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Nothing to change"), BloodborneLauncher.Core.UiText.T("These settings are the same as yours."));
                return;
            }
            var settings = changes.Count(c => c.Part == SharedSettings.Part.Settings);
            var controls = changes.Count(c => c.Part == SharedSettings.Part.Controls);
            var cheats = changes.Count(c => c.Part == SharedSettings.Part.Cheats);
            var counts = string.Join(", ", new[]
            {
                settings > 0 ? BloodborneLauncher.Core.UiText.F($"{settings} graphics and engine") : null,
                controls > 0 ? $"{controls} controls" : null,
                cheats > 0 ? $"{cheats} cheats" : null,
            }.Where(p => p != null));
            const int shown = 12;
            var list = string.Join("\n", changes.Take(shown).Select(SharedSettings.Describe)) +
                       (changes.Count > shown ? BloodborneLauncher.Core.UiText.F($"\n…and {changes.Count - shown} more") : "");
            var message = BloodborneLauncher.Core.UiText.F($"From {shared.From}: {changes.Count} changes ({counts}).\n\n{list}\n\n") +
                          "Your current settings are saved first. The launcher restarts to show the new ones.";

            HashSet<SharedSettings.Part> parts;
            if (settings > 0 && controls + cheats > 0)
            {
                var choice = Prompt.Choose(BloodborneLauncher.Core.UiText.T("Import settings"), BloodborneLauncher.Core.UiText.T(message), BloodborneLauncher.Core.UiText.T("Cancel"), BloodborneLauncher.Core.UiText.T("Keep my controls and cheats"), "Import all");
                if (choice <= 0) return;
                parts = choice == 2 ? everything : new() { SharedSettings.Part.Settings };
            }
            else if (!Prompt.Confirm(BloodborneLauncher.Core.UiText.T("Import settings"), BloodborneLauncher.Core.UiText.T(message), BloodborneLauncher.Core.UiText.T("Import")))
            {
                return;
            }
            else
            {
                parts = everything;
            }

            try
            {
                var backup = Path.Combine(Paths.ConfigDir, "settings-before-import.json");
                File.WriteAllText(backup, SharedSettings.ToJson(SharedSettings.Take(Ini, S)));
                SharedSettings.Apply(shared, Ini, S, parts);
                Ws.Log(BloodborneLauncher.Core.UiText.F($"Imported settings from {shared.From}. Your previous ones: {backup} (Import settings › Open file)."), LogLevel.Good);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Could not import"), BloodborneLauncher.Core.UiText.T(e.Message));
                return;
            }
            Restart();
        }

        private void ShowMethod() =>
            _customRows.Visibility = GameRunner.Custom(S) ? Visibility.Visible : Visibility.Collapsed;

        private void ChoosePort()
        {
            var path = PickFolder("Engine folder (with run.py and bin\\bb-probe.exe)", Paths.PortDir);
            if (path == null) return;
            if (!Paths.IsPortDir(path))
            {
                System.Windows.MessageBox.Show(Application.Current.MainWindow!, UiText.T("That folder has no run.py with Bloodborne.exe or bin\\bb-probe.exe."),
                    UiText.T("Engine folder"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            S.Set("port_dir", path);
            Ws.ResolvePort();
            Ws.SaveSoon();
            Refresh();
        }

        // ---------------------------------------------------------------- rebuild

        private static string? BuilderPath()
        {
            var beside = Path.Combine(AppContext.BaseDirectory, SourceStamp.BuilderExe);
            if (File.Exists(beside)) return beside;
            if (Paths.SourceRoot is { } root)
            {
                var installed = Path.Combine(SourceStamp.AppDirOf(root), SourceStamp.BuilderExe);
                if (File.Exists(installed)) return installed;
            }
            return null;
        }

        private static string BuildStatus()
        {
            var version = typeof(SettingsPage).Assembly.GetName().Version;
            var root = Paths.SourceRoot;
            if (root == null)
                return BloodborneLauncher.Core.UiText.F($"Version {version?.ToString(3)}. The sources (launcher/windows) are not next to this copy, so it cannot rebuild itself.");

            var stamp = SourceStamp.Read(SourceStamp.AppDirOf(root));
            var current = SourceStamp.Compute(root, SourceStamp.LauncherProject);
            var state = stamp.Launcher == current
                ? UiText.T("Up to date with the sources")
                : UiText.T("The sources changed since this build: rebuild to pick the changes up");
            var built = stamp.Built == default ? "" : BloodborneLauncher.Core.UiText.F($" · built {stamp.Built.ToLocalTime():yyyy-MM-dd HH:mm}");
            return BloodborneLauncher.Core.UiText.F($"Version {version?.ToString(3)}{built}. {state}.");
        }

        private void Rebuild(bool force)
        {
            var builder = BuilderPath();
            if (builder == null)
            {
                System.Windows.MessageBox.Show(Application.Current.MainWindow!,
                    BloodborneLauncher.Core.UiText.F($"{SourceStamp.BuilderExe} was not found next to the launcher."), UiText.T("Rebuild"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Ws.Game.Running)
            {
                System.Windows.MessageBox.Show(Application.Current.MainWindow!, UiText.T("Stop the game before rebuilding the launcher."),
                    UiText.T("Rebuild"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Ws.SaveNow();
            var args = $"--wait-pid {Environment.ProcessId}" + (force ? " --force" : "");
            Process.Start(new ProcessStartInfo(builder, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(builder)! });
            Application.Current.Shutdown();
        }
    }
}
