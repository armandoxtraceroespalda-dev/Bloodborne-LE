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
        internal static async Task DownloadAsync(string url, string path, long expected, IProgress<double>? progress, string? sha256 = null)
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? expected;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(path);
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                hash.AppendData(buffer, 0, read);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
            if (expected > 0 && done != expected)
                throw new InvalidOperationException($"The download stopped early ({done} of {expected} bytes). Try again.");
            if (sha256 != null && !Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The download is damaged: its SHA-256 does not match the release's. Try again.");
        }

        public static Version Current => new(0, 9, 39);
        public static int CurrentBeta => 2401;
        public static string Label(Version version, int beta) => beta <= 0 ? $"v{version}" :
            beta % 100 == 0 ? $"v{version} beta {beta / 100}" : $"v{version} beta {beta / 100}.{beta % 100}";
        public static string CurrentLabel => Label(Current, CurrentBeta);
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
            // A newer upstream beta identity must not rerun old WPF default migrations.
            // Stage the markers with the native settings so explicit player choices survive reloads.
            foreach (string marker in new[] { "vram_budget_beta_on", "frame_stats_beta_on" })
                if (!Settings.ContainsKey(marker))
                {
                    Settings[marker] = true;
                    _settingChanges[marker] = System.Text.Json.Nodes.JsonValue.Create(true);
                }
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
                    // The source action already changed Ini; Set would see an equal value and skip staging it.
                    _iniChanges[key] = Ini.Raw(key);
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
            if (o.Key == "native-look") return GraphicsProfiles.Selection(this);
            if (o.Key == "native-engine-preset") return EnginePreset.Current(ReadSettings());
            if (o.Key == "online_server") return SuperMedo.Selected(Ini) ? SuperMedo.Id : "huntersdream";
            if (o.Key == "ui_4k" && !UiUpscale.Ready) return "0";
            string value = o.Key == "online_password" ? ReadSettings().OnlinePassword : Read(o.Key, o.Setting, o.Default);
            if (o.Key is "cheat_vials" or "cheat_bullets" && int.TryParse(value, out int n) && n > 0)
                return "custom";
            if (o.Key is "cheat_vials_amount" or "cheat_bullets_amount" && int.TryParse(Ini.Get(o.Key[..^7]), out int amount) && amount > 0)
                return amount.ToString();
            if (o.Key == "online_pool" && string.IsNullOrEmpty(value))
                return "huntersdream";
            if (o.Key == "upscaler" && OperatingSystem.IsWindows() && value is "fsr3" or "fsr4") return "fsr";
            return value;
        }

        public void WriteOption(Option o, string value)
        {
            if (Looks.Knobs.Any(knob => knob.Key == o.Key) && Ini.Get(o.Key) != value)
                Set("native-look", GraphicsProfiles.Custom, true);

            if (o.Key == "native-engine-preset")
            {
                EditSource((settings, input) => EnginePreset.Apply(settings, value));
            }
            else if (o.Key == "online_server")
            {
                EditSource((s, ini) => { ini.Set("online_server", value == SuperMedo.Id ? value : ""); SuperMedo.Apply(ini, s); });
            }
            else if (o.Key == "online_enabled")
            {
                // An explicit user choice supersedes the temporary seamless dependency.
                EditSource((settings, ini) =>
                {
                    ini.Set(o.Key, value);
                    settings.Remove("seamless_restore_offline");
                });
            }
            else if (o.Key is "seamless_enabled" or "seamless_role")
            {
                EditSource((settings, ini) =>
                {
                    bool wasSeamless = ini.Get("seamless_enabled") == "1";
                    if (o.Key == "seamless_enabled")
                    {
                        if (value == "1" && !wasSeamless)
                            settings.Set("seamless_restore_offline", ini.Get("online_enabled") != "1" ? "true" : "false");
                        if (value == "1") ini.Set("online_enabled", "1");
                        else
                        {
                            if (wasSeamless && settings.Str("seamless_restore_offline") == "true")
                                ini.Set("online_enabled", "0");
                            settings.Remove("seamless_restore_offline");
                        }
                    }
                    ini.Set(o.Key, value);
                    if (ini.Get("seamless_role", "guest") == "host" &&
                        (ini.Get("seamless_host_room").Length == 0 || settings.SeamlessHostPassword.Length == 0))
                    {
                        ini.Set("seamless_host_room", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(10)).ToLowerInvariant());
                        settings.SeamlessHostPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
                    }
                });
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
