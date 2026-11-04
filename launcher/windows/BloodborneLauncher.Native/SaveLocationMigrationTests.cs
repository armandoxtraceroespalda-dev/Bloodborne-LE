using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using System.Text.Json.Nodes;

namespace BloodborneLauncher.Native;
internal static class SaveLocationMigrationTests
{
    internal static void Run()
    {
        static void Check(bool ok, string text)
        {
            if (!ok) throw new InvalidOperationException(text);
            Console.WriteLine("PASS " + text);
        }
        string root = Path.Combine(Path.GetTempPath(), "save-location-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = (Paths.IniPath, Paths.ConfigDir, Paths.ConfigFile, Paths.DataDir, Paths.PortDir, Workspace.Current.Ini);
        try
        {
            string old = Path.Combine(root, "BetterKBM-engine-update36", "user");
            string engine = Path.Combine(root, "current", "engine");
            string local = Path.Combine(engine, "user");
            void Write(string relative, string contents)
            {
                string file = Path.Combine(old, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, contents);
            }
            Write("savedata/SPRJ0005/userdata0000", "main-save");
            Write("profiles/Player/savedata/SPRJ0005/userdata0000", "player-save");
            Write("launcher-backups/Main/2026-01-01_00-00-00/savedata/SPRJ0005/userdata0000", "backup-save");
            Write("trophy.log", "earned");
            Directory.CreateDirectory(engine);
            string config = Path.Combine(root, "settings.json"), ini = Path.Combine(engine, "lantern.ini"), stage = Path.Combine(root, "staged");
            File.WriteAllText(ini, "key.cross=E\noutput_res=2560x1440\n");
            ProfileSession.AtomicJson(config, new JsonObject { ["user_dir"] = old, ["port_dir"] = engine, ["text_language"] = "1", ["unrelated"] = "keep" });
            string[] args = { "--config", config, "--ini", ini, "--profile", stage, "--assets", root };
            var profile = new ProfileSession(args);
            Check(profile.SaveLocationMigrationError == null && profile.Str("user_dir") == local, "legacy engine user path is redirected to the current data folder");
            Check(File.ReadAllText(Path.Combine(local, "savedata/SPRJ0005/userdata0000")) == "main-save", "Main savedata survives migration");
            Check(File.ReadAllText(Path.Combine(local, "profiles/Player/savedata/SPRJ0005/userdata0000")) == "player-save", "all player profiles survive migration");
            Check(File.ReadAllText(Path.Combine(local, "launcher-backups/Main/2026-01-01_00-00-00/savedata/SPRJ0005/userdata0000")) == "backup-save", "existing backups survive migration");
            Check(File.ReadAllText(Path.Combine(local, "trophy.log")) == "earned" && Directory.Exists(old), "achievements survive and the original folder is retained");
            Check(ProfileSession.ReadJson(config)["user_dir"]!.ToString() == local && ProfileSession.ReadJson(config)["unrelated"]!.ToString() == "keep", "only the corrected save key is persisted without needing Play");
            var settings = profile.SourceSettings();
            Check(GameEnvironment.UserDir(settings) == local && SaveBackups.Folder(settings).StartsWith(local + Path.DirectorySeparatorChar), "game and backups resolve the same migrated root");
            Check(SaveBackups.Create(settings) is { } backup && backup.StartsWith(local + Path.DirectorySeparatorChar), "a new backup is created in the current root");
            Directory.Delete(old, true);
            profile = new ProfileSession(args);
            SaveBackups.Create(profile.SourceSettings());
            Check(profile.Str("user_dir") == local && !Directory.Exists(old), "restart and backup cannot recreate the retired engine folder");
            Check(profile.Ini.Get("key.cross") == "E" && profile.Ini.Get("output_res") == "2560x1440", "controls and graphics configuration remain unchanged");
            string custom = Path.Combine(root, "My saves");
            Check(LegacySaveLocation.Migrate(custom, local) == null, "arbitrary custom save folders are never redirected");
            Check(!LegacySaveLocation.IsLegacy(Path.Combine(root, "BetterKBM-engine-update36", "custom")), "only the former default user folder qualifies");
            Write("savedata/SPRJ0005/userdata0000", "different-old-save");
            bool refused = false;
            try { LegacySaveLocation.Migrate(old, local); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(Path.Combine(local, "savedata/SPRJ0005/userdata0000")) == "main-save" && File.ReadAllText(Path.Combine(old, "savedata/SPRJ0005/userdata0000")) == "different-old-save", "different saves in both locations are never overwritten");
            Check(LegacySaveLocation.Migrate(local, local) == null, "current local user folder does not migrate repeatedly");
            string emptyOld = Path.Combine(root, "BetterKBM-engine-expanded", "user");
            Check(LegacySaveLocation.Migrate(emptyOld, local) == local && !Directory.Exists(emptyOld), "a missing obsolete folder is corrected without recreating it");
            profile.Set("user_dir", old, true);
            profile.Set("native_user_dir_selected", "true", true);
            profile.MigrateLegacySaves();
            Check(profile.Str("user_dir") == old && profile.SaveLocationMigrationError == null, "a folder explicitly chosen with Browse remains pinned");
            profile.Set("native_user_dir_selected", "false", true);
            profile.MigrateLegacySaves();
            Check(profile.SaveLocationMigrationError != null && profile.Str("user_dir") == old, "a conflicting migration reports its error and keeps the original setting");
        }
        finally
        {
            Paths.IniPath = previous.Item1; Paths.ConfigDir = previous.Item2; Paths.ConfigFile = previous.Item3;
            Paths.DataDir = previous.Item4; Paths.EngineDir = previous.Item5; Workspace.Current.Ini = previous.Item6;
            Directory.Delete(root, true);
        }
    }
}
