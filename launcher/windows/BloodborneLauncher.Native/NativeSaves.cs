using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    sealed record NativeSaveProfile(string Name, bool HasSaves, int Backups, TimeSpan Played);
    List<NativeSaveProfile>? _saveProfiles;
    List<SaveBackups.Backup>? _saveBackups;
    string _saveViewSignature = "";
    bool _readingSaves;
    int _saveReadGeneration;

    void ReleaseNativeSaveViews()
    {
        ++_saveReadGeneration;
        _saveProfiles = null;
        _saveBackups = null;
        _saveViewSignature = "";
        _readingSaves = false;
        ReleaseNativeSaveEditor();
    }

    void InvalidateNativeSaves()
    {
        _readingSaves = false;
        ++_saveReadGeneration;
        _saveProfiles = null;
        _saveBackups = null;
        _saveViewSignature = "";
        Rebuild();
    }

    bool SaveGameIsRunning() => _game?.Running == true || SaveStore.GameProcessRunning();
    void RequireClosedSaveGame()
    {
        if (SaveGameIsRunning()) throw new InvalidOperationException(UiText.T("Close the game first: saves can't be changed while it runs."));
    }

    void ReadNativeSaves(LauncherSettings settings, string signature)
    {
        if (_readingSaves || _busy) return;
        _readingSaves = true;
        int generation = ++_saveReadGeneration;
        Background(async () =>
        {
            var result = await Task.Run(() =>
            {
                var profiles = PlayerProfiles.All(settings).Select(name =>
                {
                    string data = Path.Combine(PlayerProfiles.Folder(settings, name), "savedata");
                    string copies = SaveGuard.ProfileCopies(GameEnvironment.SavesRoot(settings), name);
                    return new NativeSaveProfile(name, SaveGuard.HasFiles(data),
                        SaveBackups.ListWithSafetyCopies(SaveBackups.FolderOf(settings, name), copies).Count,
                        SessionStats.Playtime(settings, name));
                }).ToList();
                var backups = SaveBackups.ListWithSafetyCopies(SaveBackups.Folder(settings),
                    SaveGuard.ProfileCopies(GameEnvironment.SavesRoot(settings), PlayerProfiles.Current(settings)));
                return (profiles, backups);
            });
            return () =>
            {
                if (generation != _saveReadGeneration || _page != 2) return;
                _readingSaves = false;
                _saveProfiles = result.profiles;
                _saveBackups = result.backups;
                _saveViewSignature = signature;
                _navigationMemory.Request();
                Rebuild();
            };
        });
    }

    void DrawNativeSaves(float x, float width, ref float y)
    {
        var settings = _profile.ReadSettings();
        string current = PlayerProfiles.Current(settings);
        string signature = GameEnvironment.SavesRoot(settings) + "|" + current + "|" + string.Join('|', PlayerProfiles.All(settings));
        if (_saveProfiles == null || _saveViewSignature != signature) ReadNativeSaves(_profile.SourceSettings(), signature);
        PageGroup("Profiles", x, width, ref y);
        if (_saveProfiles == null)
        {
            TextAt("Loading...", new Rect(x + 10 * _scale, y, width - 20 * _scale, 34 * _scale), 18);
            y += 44 * _scale;
        }
        else foreach (var profile in _saveProfiles)
        {
            var selected = profile;
            bool active = string.Equals(profile.Name, current, StringComparison.OrdinalIgnoreCase);
            string detail = UiText.T(profile.HasSaves ? "Has saves" : "No saves yet") + " · " + profile.Backups + " " + UiText.T("Backups");
            if (profile.Played.TotalMinutes >= 1) detail += " · " + SessionStats.Duration(profile.Played);
            var buttons = new List<PageButton>();
            if (active)
            {
                if (!PlayerProfiles.IsMain(profile.Name))
                {
                    buttons.Add(new("Rename", "save-profile-rename", () => { RequireClosedSaveGame(); RenameReferencePlayer(); InvalidateNativeSaves(); }, "\uE8AC"));
                    buttons.Add(new("Remove", "save-profile-remove", () => { RequireClosedSaveGame(); RemoveReferencePlayer(); InvalidateNativeSaves(); }, "\uE74D"));
                }
            }
            else buttons.Add(new("Switch", "save-profile-" + selected.Name, () =>
            {
                RequireClosedSaveGame();
                _profile.Set("player_profile", PlayerProfiles.IsMain(selected.Name) ? "" : selected.Name, true);
                _trophies = null;
                InvalidateNativeSaves();
            }, "\uE77B"));
            ReferenceCard(profile.Name + (active ? " · " + UiText.T("Playing") : ""), detail, "\uE77B", x, width, ref y, buttons, true);
        }
        Button("New profile", new Rect(x + 10 * _scale, y, Math.Min(width - 20 * _scale, 230 * _scale), 38 * _scale),
            "save-profile-new", () => Run(() => { RequireClosedSaveGame(); PageAction("player-new"); InvalidateNativeSaves(); }), icon: "\uE710");
        y += 56 * _scale;
        PageGroup("Backups", x, width, ref y);
        var enabled = new Option("", "Back up saves before playing", "save_backups", OptionKind.Toggle, true, Default: "true");
        DrawOption(enabled, new Rect(x, y, width, 52 * _scale));
        y += 58 * _scale;
        Button("Create backup", new Rect(x + 10 * _scale, y, 210 * _scale, 38 * _scale), "save-backup-create", () => Run(() =>
        {
            RequireClosedSaveGame();
            SaveBackups.Create(_profile.SourceSettings());
            InvalidateNativeSaves();
        }), icon: "\uE74E");
        Button("Open backups folder", new Rect(x + 230 * _scale, y, Math.Min(250 * _scale, width - 240 * _scale), 38 * _scale), "save-backup-folder", () => OpenPath(SaveBackups.Folder(_profile.SourceSettings()), true), icon: "\uE8B7");
        y += 52 * _scale;
        if (_saveBackups is { Count: > 0 }) foreach (var backup in _saveBackups)
        {
            var chosen = backup;
            string kind = backup.Label ?? UiText.T(backup.SafetyCopy ? "Safety copy" : backup.MadeBeforeRestore ? "Before a restore" : backup.MadeDuringPlay ? "While playing" : "Before playing");
            ReferenceCard(backup.When.ToString("g"), kind + $" · {backup.Bytes / 1048576.0:0.0} MB", "\uE81C", x, width, ref y,
                new[] { new PageButton("Restore", "save-backup-" + chosen.Dir, () =>
                {
                    RequireClosedSaveGame();
                    if (!Confirm(UiText.T("Restore backup"))) return;
                    SaveBackups.Restore(_profile.SourceSettings(), chosen);
                    _trophies = null;
                    InvalidateNativeSaves();
                }, "\uE7A7") }, true);
        }
        else if (!_readingSaves)
        {
            TextAt("No backups yet.", new Rect(x + 10 * _scale, y, width - 20 * _scale, 38 * _scale), 18);
            y += 48 * _scale;
        }
        PageGroup("Other", x, width, ref y);
        ReferenceCard("Import saves", "From shadPS4, another Bloodborne port, or a PS4 save exported decrypted (Apollo Save Tool, Save Wizard). This profile's saves are backed up first", "\uE8B5", x, width, ref y,
            new[] { new PageButton("Import...", "save-import", () => { RequireClosedSaveGame(); ImportReferenceSaves(); InvalidateNativeSaves(); }, "\uE896") });
        FolderCard("Saves folder", "user_dir", GameEnvironment.SavesRoot(settings), x, width, ref y, true);
        Button("Refresh", new Rect(x + 10 * _scale, y, 160 * _scale, 38 * _scale), "save-refresh", InvalidateNativeSaves, icon: "\uE72C");
        y += 54 * _scale;
    }

    bool GuardNativeSaves(LauncherSettings settings)
    {
        if (_profile.SaveLocationMigrationError != null)
        {
            _profile.MigrateLegacySaves();
            settings = _profile.SourceSettings();
        }
        if (_profile.SaveLocationMigrationError is { } migrationError)
        {
            ShowMessage(migrationError, UiText.T("Saves folder"), 0);
            return false;
        }
        if (SaveGuard.HasFiles(SaveBackups.SaveData(settings))) return true;
        var found = SaveGuard.Elsewhere(GameEnvironment.SavesRoot(settings), PlayerProfiles.Current(settings)).FirstOrDefault();
        if (found == null) return true;
        (bool Start, bool Changed) outcome = (false, false);
        _profile.EditSource((state, _) => outcome = SaveGuard.Ask(state, found, (title, body, choices) =>
        {
            int answer = ShowMessage(body + "\n\n" + UiText.T("Yes") + ": " + choices[2] + "\n" + UiText.T("No") + ": " + choices[1], title, 0x23);
            return answer == 6 ? 2 : answer == 7 ? 1 : 0;
        }, (title, body) => ShowMessage(body, title, 0)));
        if (outcome.Changed)
        {
            _profile.MigrateLegacySaves();
            if (_profile.SaveLocationMigrationError is { } recoveryError)
            {
                ShowMessage(recoveryError, UiText.T("Saves folder"), 0);
                return false;
            }
            _trophies = null;
            InvalidateNativeSaves();
        }
        return outcome.Start;
    }
}
