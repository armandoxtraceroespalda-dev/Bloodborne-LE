using System.Text.Json.Nodes;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Core
{
    public enum LogLevel { Info, Step, Good, Warn, Bad }
    public static class LauncherDiagnostics
    {
        public static void Record(string message, LogLevel level)
        {
            try
            {
                string file = Path.Combine(Paths.ConfigDir, "launcher-diagnostics.log");
                Directory.CreateDirectory(Paths.ConfigDir);
                if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Move(file, file + ".previous", true);
                File.AppendAllText(file, DateTime.UtcNow.ToString("O") + " " + level + " " + message + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    public static class Updater
    {
        public static int CurrentBeta => 0;
        public static string CurrentLabel => "Bloodborne Launcher";
        public static string? LocalEngine()
        {
            try
            {
                var stamp = JsonNode.Parse(File.ReadAllText(Path.Combine(Paths.EngineDir, "out", "better-kbm-build.json")));
                return stamp?["upstream_release"]?.ToString() ?? stamp?["version"]?.ToString();
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException) { return null; }
        }
    }

    public sealed class Workspace
    {
        public static Workspace Current { get; } = new();
        public IniFile Ini { get; set; } = new();
    }

    // The upstream game runner only needs the directory, not WPF image objects.
    public static class MenuAssets
    {
        public static string DirectoryPath { get; set; } = "";
    }

}

namespace BloodborneLauncher.Native.Backend
{
    public sealed partial class ProfileSession
    {
        readonly HashSet<string> _sidecarChanges = new(StringComparer.Ordinal);
        string _sidecarDirectory = "";
        public T WithSidecars<T>(Func<T> action, string? changed = null)
        {
            string original = Paths.DataDir;
            try
            {
                Paths.DataDir = _sidecarDirectory;
                T result = action();
                if (changed != null)
                    _sidecarChanges.Add(changed);
                return result;
            }
            finally
            {
                Paths.DataDir = original;
            }
        }

        LauncherSettings? _readSettings;
        public long SettingsSnapshotLoads { get; private set; }
        public long SettingsClones { get; private set; }
        // Rendering callers treat this snapshot as read-only; editing/launch callers get a clone.
        internal LauncherSettings ReadSettings()
        {
            if (_readSettings != null) return _readSettings;
            AtomicJson(Paths.ConfigFile, Settings);
            var snapshot = new LauncherSettings();
            snapshot.Load();
            SettingsSnapshotLoads++;
            return _readSettings = snapshot;
        }
        void InvalidateSettingsSnapshot() => _readSettings = null;
        public LauncherSettings SourceSettings()
        {
            var copy = new LauncherSettings();
            foreach (var pair in ReadSettings().All)
                if (pair.Value != null) copy.SetNode(pair.Key, pair.Value);
                else copy.Set(pair.Key, (string)null!);
            SettingsClones++;
            return copy;
        }

        public void EditSource(Action<LauncherSettings, IniFile> action)
        {
            var s = SourceSettings();
            var before = s.All.ToDictionary(p => p.Key, p => p.Value?.ToJsonString());
            var old = Ini.Values.ToDictionary(p => p.Key, p => p.Value);
            action(s, Ini);
            foreach (var key in old.Keys.Union(Ini.Values.Select(p => p.Key)).ToArray())
                if (old.GetValueOrDefault(key) != Ini.Raw(key))
                    Set(key, Ini.Raw(key));
            var after = s.All.ToDictionary(p => p.Key, p => p.Value);
            foreach (var key in before.Keys.Union(after.Keys))
                if (before.GetValueOrDefault(key) != after.GetValueOrDefault(key)?.ToJsonString())
                {
                    var node = after.GetValueOrDefault(key);
                    if (node == null)
                        Settings.Remove(key);
                    else
                        Settings[key] = node.DeepClone();
                    _settingChanges[key] = node?.DeepClone();
                    InvalidateSettingsSnapshot();
                }
        }

        public string ModsRoot => Str("mods_dir")is { Length: > 0 } mods ? Paths.Expand(mods) : Path.Combine(Paths.DataDir, "mods");
        public string PatchesRoot => Str("patches_dir")is { Length: > 0 } patches ? Paths.Expand(patches) : Path.Combine(Paths.EngineDir, "patches");
        public string InputDevice => Ini.Get("input_mode", "keyboard_mouse");

        public void Input(string device)
        {
            Set("input_mode", device);
            Set("pc_controls", "1");
            Set("mouse_camera", device == "keyboard_mouse" ? "1" : "0");
            Set("mouse_menu", "1");
            Set("button_prompts", device == "keyboard_mouse" ? "keyboard" : "controller");
        }

        public IReadOnlyList<ControlProfiles.Profile> Profiles() => ControlProfiles.All(InputDevice);
        public void UseProfile(ControlProfiles.Profile p) => EditSource((s, ini) => ControlProfiles.Apply(s, ini, p));
        public ControlProfiles.Profile CaptureProfile(string name) => new(name, "", "", Bindings.Current(Ini), Pad: PadMap.Current(Ini), InputDevice: InputDevice);
        public void Bind(ControlDef c, string chord) => EditSource((s, ini) => Bindings.Assign(ini, c, chord, false));
        public void ResetBindings() => EditSource((s, ini) =>
        {
            if (InputDevice == "keyboard_mouse")
                ControlProfiles.Apply(s, ini, ControlProfiles.BuiltIns.First(p => p.Name == ControlProfiles.DarkSouls3Name));
            else
                PadMap.ResetAll(ini);
        });
        public IReadOnlyList<Hardware.Change> Recommendations() => Hardware.Detect().Recommend(SourceSettings(), Ini);
        public void Recommend() => EditSource((s, ini) =>
        {
            foreach (var c in Hardware.Detect().Recommend(s, ini))
                c.Apply();
        });
        public string ExportSettings() => SharedSettings.ToJson(SharedSettings.Take(Ini, SourceSettings()));
        public void ImportSettings(string json)
        {
            var snapshot = SharedSettings.Parse(json);
            EditSource((s, ini) => SharedSettings.Apply(snapshot, ini, s, new HashSet<SharedSettings.Part>(Enum.GetValues<SharedSettings.Part>())));
        }

        public void HideCheats()
        {
            foreach (var key in Ini.Values.Select(p => p.Key).Where(k => k.StartsWith("cheat_", StringComparison.Ordinal)).ToArray())
                Set(key, "0");
            foreach (var key in new[]
            {
                "cheat_vials",
                "cheat_bullets",
                "cheat_gun_range"
            }

            )
                Set(key, "off");
            Set("cheat_echo_multiplier", "1");
            Set("cheats_unlocked", "false", true);
        }

        public bool IsOn(Option option)
        {
            string value = Value(option);
            return option.Key.StartsWith("env:", StringComparison.Ordinal)
                ? EngineSwitches.All.First(item => item.Key == option.Key).On == value
                : value is "1" or "true";
        }

        public string Value(Option o)
        {
            if (o.Key == "native-engine-preset") return EnginePreset.Current(ReadSettings());
            string value = o.Key == "online_password" ? ReadSettings().OnlinePassword : Read(o.Key, o.Setting, o.Default);
            if (o.Key is "cheat_vials" or "cheat_bullets" && int.TryParse(value, out int n) && n > 0)
                return "custom";
            if (o.Key is "cheat_vials_amount" or "cheat_bullets_amount" && int.TryParse(Ini.Get(o.Key[..^7]), out int amount) && amount > 0)
                return amount.ToString();
            if (o.Key == "online_pool" && string.IsNullOrEmpty(value))
                return "huntersdream";
            return value;
        }

        public void WriteOption(Option o, string value)
        {
            if (o.Key == "native-engine-preset")
            {
                EditSource((settings, input) => EnginePreset.Apply(settings, value));
            }
            else if (o.Key == "online_password")
            {
                EditSource((s, ini) => s.OnlinePassword = value);
            }
            else if (o.Key.StartsWith("env:", StringComparison.Ordinal))
            {
                var item = EngineSwitches.All.First(i => i.Key == o.Key);
                EditSource((s, ini) => EngineSwitches.Set(s, item, value));
            }
            else if (o.Key is "cheat_vials" or "cheat_bullets")
                Set(o.Key, value == "custom" ? Str(o.Key + "_amount", "20") : value);
            else
            {
                Set(o.Key, value, o.Setting);
                if (o.Key is "cheat_vials_amount" or "cheat_bullets_amount" && int.TryParse(Ini.Get(o.Key[..^7]), out int n) && n > 0)
                    Set(o.Key[..^7], value);
            }
        }
    }
}
