using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BloodborneLauncher.Core;

// IniFile needs only this path. The production Paths class also migrates user
// profiles and knows the WPF package; the preview deliberately does neither.
namespace BloodborneLauncher.Core
{
    public static class Paths
    {
        public static string IniPath { get; set; } = "";
        public static string ConfigFile { get; set; } = "";
        public static string ConfigDir { get; set; } = "";
        public static string DataDir { get; set; } = "";
        static string _portDir = "";
        public static string EngineDir { get => EngineDirOf(_portDir); set => _portDir = value; }
        public static string PortDir => _portDir;
        public static string EngineDirOf(string dir) => Directory.Exists(Path.Combine(dir, "engine", "out")) ? Path.Combine(dir, "engine") : dir;
        static string ProgramName(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
        public static string Start => Path.Combine(EngineDir, "out", ProgramName("bb-start"));
        public static string Python => Path.Combine(EngineDir, "tools", "python", "python.exe");
        public static bool IsPortDir(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string engine = EngineDirOf(dir);
            return File.Exists(Path.Combine(engine, "out", ProgramName("bb-probe"))) &&
                (File.Exists(Path.Combine(engine, "out", ProgramName("bb-start"))) || File.Exists(Path.Combine(engine, "scripts", "run_windows.py")));
        }
        public static string BinariesDir => Path.Combine(EngineDir, "out");

        public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);
    }
}

namespace BloodborneLauncher.Native.Backend
{
    public sealed partial class ProfileSession
    {
        public JsonObject Settings { get; }
        IReadOnlyDictionary<string, string>? _help;
        public IReadOnlyDictionary<string, string> Help => _help ??= LoadHelp();
        public bool HelpLoaded => _help != null;
        static IReadOnlyDictionary<string, string> LoadHelp()
        {
            var audit = Path.Combine(AppContext.BaseDirectory, "LauncherApp", "upstream-audit.json");
            if(!File.Exists(audit))audit=Path.Combine(AppContext.BaseDirectory,"upstream-audit.json");
            try
            {
                return File.Exists(audit)
                    ? JsonNode.Parse(File.ReadAllText(audit))?["help"]?.Deserialize<Dictionary<string, string>>() ?? new()
                    : new Dictionary<string, string>();
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                return new Dictionary<string, string>();
            }
        }
        public IniFile Ini { get; } = new();
        public string ConfigPath { get; }
        public string OriginalIni { get; }
        public string SandboxIni { get; }

        private readonly string _previewFile;
        public string Assets { get; }

        private readonly Dictionary<string, string?> _iniChanges = new();
        private readonly Dictionary<string, JsonNode?> _settingChanges = new();
        public bool Dirty => _iniChanges.Count + _settingChanges.Count + _sidecarChanges.Count > 0;

        public ProfileSession(string[] args)
        {
            string Arg(string key, string fallback = "")
            {
                int i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            ConfigPath = Arg("--config", new[] { Path.Combine(appData, "Lantern Engine", "settings.json"), Path.Combine(appData, "bbport-launcher", "settings.json") }.FirstOrDefault(File.Exists) ?? Path.Combine(appData, "Lantern Engine", "settings.json"));
            Settings = ReadJson(ConfigPath);
            string port = Environment.ExpandEnvironmentVariables(Str("port_dir"));
            string data = Environment.GetEnvironmentVariable("BB_DATA_DIR") ?? port;
            OriginalIni = Arg("--ini", Environment.GetEnvironmentVariable("BB_CONFIG") ?? new[] { Path.Combine(data, "lantern.ini"), Path.Combine(data, "bbport.ini"), Path.Combine(port, "engine", "lantern.ini") }.FirstOrDefault(File.Exists) ?? Path.Combine(port, "lantern.ini"));
            string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(OriginalIni) + "|" + ConfigPath)))[..16];
            var work = Arg("--profile", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bloodborne Native Preview", "profiles", identity));
            Directory.CreateDirectory(work);
            _previewFile = Path.Combine(work, "staged.json");
            SandboxIni = Path.Combine(work, "preview.ini");
            if (File.Exists(OriginalIni))
                File.Copy(OriginalIni, SandboxIni, true);
            else
                File.WriteAllText(SandboxIni, "# Isolated native preview\n");
            Paths.IniPath = SandboxIni;
            Paths.ConfigDir = Path.GetDirectoryName(ConfigPath)!;
            Paths.ConfigFile = Path.Combine(work, "source-settings.json");
            Paths.DataDir = Path.GetDirectoryName(Path.GetFullPath(OriginalIni))!;
            Paths.EngineDir = port;
            Workspace.Current.Ini = Ini;
            Ini.Load();
            var staged = ReadJson(_previewFile);
            _sidecarDirectory = Path.Combine(work, "sidecars");
            Directory.CreateDirectory(_sidecarDirectory);
            foreach (var file in new[]
            {
                "mods.json",
                "patches.json"
            }

            )
            {
                bool pending = staged["files"] is JsonArray list && list.Any(n => n?.ToString() == file);
                string target = Path.Combine(_sidecarDirectory, file), source = Path.Combine(Paths.DataDir, file);
                if (pending && File.Exists(target))
                    _sidecarChanges.Add(file);
                else if (File.Exists(source))
                    File.Copy(source, target, true);
                else if (File.Exists(target))
                    File.Delete(target);
            }

            if (staged["ini"] is JsonObject stagedIni)
                foreach (var kv in stagedIni)
                    Set(kv.Key, kv.Value?.ToString());
            if (staged["settings"] is JsonObject stagedSettings)
                foreach (var kv in stagedSettings)
                {
                    if (kv.Value == null)
                        Settings.Remove(kv.Key);
                    else
                        Settings[kv.Key] = kv.Value.DeepClone();
                    _settingChanges[kv.Key] = kv.Value?.DeepClone();
                }

            Assets = Path.GetFullPath(Arg("--assets", Path.Combine(AppContext.BaseDirectory, "gui")));
            UiText.UseTextLanguage(Str("text_language", Str("language", "1")));
        }

        public static JsonObject ReadJson(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new();
        public string Str(string key, string fallback = "") => Settings[key] is JsonValue v ? v.ToString() : fallback;
        public string Read(string key, bool setting = false, string fallback = "0") => setting ? Str(key, fallback) : Ini.Get(key, fallback);
        public void Set(string key, string? value, bool setting = false)
        {
            if (setting)
            {
                if (Str(key) == (value ?? "") && (value != null || !Settings.ContainsKey(key))) return;
                InvalidateSettingsSnapshot();
                JsonNode? node = value is "true" or "false" ? JsonValue.Create(value == "true") : JsonValue.Create(value);
                Settings[key] = node?.DeepClone();
                _settingChanges[key] = node;
                if (key == "port_dir")
                    Paths.EngineDir = value ?? "";
            }
            else
            {
                if (Ini.Raw(key) == value) return;
                Ini.Set(key, value);
                _iniChanges[key] = value;
            }
        }

        public void SavePreview()
        {
            Paths.IniPath = SandboxIni;
            Ini.Save();
            var ini = new JsonObject();
            var settings = new JsonObject();
            foreach (var kv in _iniChanges)
                ini[kv.Key] = kv.Value;
            foreach (var kv in _settingChanges)
                settings[kv.Key] = kv.Value?.DeepClone();
            AtomicJson(_previewFile, new JsonObject { ["ini"] = ini, ["settings"] = settings, ["files"] = new JsonArray(_sidecarChanges.Select(n => (JsonNode? )JsonValue.Create(n)).ToArray()) });
        }

        public void ApplyToGame()
        {
            // Merge only edited keys into the latest disk state, preserving game changes.
            try
            {
                if (!File.Exists(OriginalIni))
                    throw new FileNotFoundException("No existing game configuration was found. Pass --ini <lantern.ini> before applying settings.");
                Paths.IniPath = OriginalIni;
                var target = new IniFile();
                target.Load();
                foreach (var kv in _iniChanges)
                    target.Set(kv.Key, kv.Value);
                if (_iniChanges.Count > 0)
                    target.Save();
                if (_settingChanges.Count > 0)
                {
                    var json = ReadJson(ConfigPath);
                    foreach (var kv in _settingChanges)
                        if (kv.Value == null)
                            json.Remove(kv.Key);
                        else
                            json[kv.Key] = kv.Value.DeepClone();
                    AtomicJson(ConfigPath, json);
                }

                foreach (var file in _sidecarChanges)
                    AtomicJson(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(OriginalIni))!, file), ReadJson(Path.Combine(_sidecarDirectory, file)));
                _sidecarChanges.Clear();
                _iniChanges.Clear();
                _settingChanges.Clear();
                if (File.Exists(_previewFile))
                    File.Delete(_previewFile);
            }
            finally
            {
                Paths.IniPath = SandboxIni;
            }
        }

        public static void AtomicJson(string path, JsonObject json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".native.tmp";
            try
            {
                File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }


    }

    public enum OptionKind
    {
        Toggle,
        Choice,
        Slider,
        Text,
        Folder,
        Action,
        Password
    }

    public sealed record Option(string Section, string Title, string Key, OptionKind Kind, bool Setting = false, IReadOnlyList<Choice>? Choices = null, double Min = 0, double Max = 1, double Step = .05, string Default = "0", string? Gate = null, string? Visible = null, string? Hint = null)
    {
        public bool Heading { get; init; }
        public string Icon { get; init; } = "";
    }
    public static class GraphicsSchema
    {
        static Option EngineGraphicsOption(string key, string section)
        {
            var item = EngineSwitches.All.First(i => i.Env == key);
            return new(section, item.Title, item.Key, item.Kind == SwitchKind.Choice ? OptionKind.Choice : OptionKind.Toggle,
                true, item.Options, Default: item.Default, Hint: EngineSwitches.Describe(item));
        }

        public static IReadOnlyList<Option> Build()
        {
            var o = new List<Option>();
            void T(string s, string t, string k, bool setting = false, string d = "0", string? gate = null) => o.Add(new(s, t, k, OptionKind.Toggle, setting, Default: d, Gate: gate));
            void C(string s, string t, string k, IReadOnlyList<Choice> c, bool setting = false, string d = "0", string? gate = null, string? visible = null) => o.Add(new(s, t, k, OptionKind.Choice, setting, c, Default: d, Gate: gate, Visible: visible));
            void N(string s, string t, string k, double min, double max, double step = .05, string d = "1", string? gate = null, string? visible = null) => o.Add(new(s, t, k, OptionKind.Slider, Min: min, Max: max, Step: step, Default: d, Gate: gate, Visible: visible));
            C("Screen", "Monitor", "display_device", new[] { Choice.Literal("Default (Windows primary monitor)", "") }.Concat(OperatingSystem.IsWindows() ? WindowsMonitors.All().Select(m => Choice.Literal(m.Label, m.Device)) : Array.Empty<Choice>()).ToArray(), true, "");
            C("Screen", "Frame rate", "fps_mode", Choices.FpsModes, true, "60");
            C("Screen", "Output resolution", "output_res", Choices.OutputResolutions, d: "1920x1080");
            o.Add(new("Screen", "Sharp UI", "exp_sharp_ui", OptionKind.Toggle, Setting: true, Default: "false",
                Hint: "Draws menus, HUD and text at the output resolution for a sharper interface above 1080p. Applied on the next launch."));
            C("Screen", "Live resolution change", "live_resolution", Choices.LiveResolution);
            T("Screen", "Fullscreen", "fullscreen");
            T("Screen", "Widescreen", "widescreen", d: "1");
            C("Screen", "Vsync", "present_mode", Choices.PresentModes, true, "Mailbox");
            o.Add(EngineGraphicsOption("BB_ANISO", "Screen"));
            C("Upscaler", "Upscaler", "upscaler", Choices.Upscalers, d: "fsr4");
            C("Upscaler", "Quality preset", "preset", Choices.Presets, d: "4");
            N("Upscaler", "Render scale (Custom preset)", "render_scale", 1, 3, .05, "1.30", visible: "custom");
            T("Upscaler", "Sharpening (RCAS)", "sharpen", d: "1");
            N("Upscaler", "Sharpening strength", "sharpness", 0, 2, .05, "0.50");
            N("Upscaler", "Texture mip bias", "mip_bias", -2, 1, .05, "0");
            T("Upscaler", "Object motion vectors", "object_motion", d: "1");
            o.Add(EngineGraphicsOption("BB_UPSCALER_NO_BLUR", "Upscaler"));
            C("Upscaler", "Frame generation", "frame_gen", Choices.FrameGeneration, d: "off");
            T("Upscaler", "Frame generation: even pacing (Uncapped)", "frame_gen_cap", d: "1");
            C("DLSS options", "DLSS model", "dlss_preset", Choices.DlssModels, d: "k", visible: "dlss");
            T("DLSS options", "DLSS auto exposure", "dlss_auto_exposure");
            T("DLSS options", "DLSS sharper textures", "dlss_mip_bias", d: "1");
            C("DLSS options", "DLSS generated frames", "frame_gen_frames", Choices.FrameGenFrames, d: "2");
            T("DLSS options", "DLSS dynamic frame generation", "frame_gen_dynamic");
            C("DLSS options", "NVIDIA Reflex", "reflex", Choices.Reflex);
            C("Game effects", "Model detail", "model_lod", Choices.ModelLod);
            T("Game effects", "Vignette", "graphics_vignette", d: "1");
            foreach (var e in Choices.Effects.Where(e => e.Key is not ("skip_intro" or "debug_camera")))
                T("Game effects", e.SourceTitle, e.Key, d: e.Default ? "1" : "0");
            T("Picture", "Picture tuning", "graphics_controls");
            foreach (var n in new[]
            {
                ("Ambient occlusion strength", "graphics_ao_strength", 0d, 3d, 1d),
                ("Shadow distance", "graphics_shadow_scale", .5, 4, 1),
                ("Bloom", "graphics_bloom", 0, 3, 1),
                ("Saturation", "graphics_saturation", 0, 3, 1),
                ("Brightness", "graphics_brightness", 0, 3, 1),
                ("Contrast", "graphics_contrast", .8, 1.2, 1),
                ("Hue", "graphics_hue", -180, 180, 0),
                ("Fog", "graphics_fog", 0, 3, 1)
            }

            )
                N("Picture", n.Item1, n.Item2, n.Item3, n.Item4, n.Item2 == "graphics_hue" ? 1 : .01, n.Item5.ToString(CultureInfo.InvariantCulture), "graphics_controls");
            T("HDR", "HDR output", "hdr");
            C("HDR", "HDR source", "hdr_mode", Choices.HdrMode, d: "native", gate: "hdr");
            N("HDR", "Paper white", "hdr_paper_white", 10, 500, 1, "200", "hdr");
            N("HDR", "Peak brightness", "hdr_peak", 10, 4000, 10, "1000", "hdr");
            C("HDR", "Highlights", "hdr_boost", Choices.HdrBoost, d: "0.25", gate: "hdr");
            C("HDR", "UI brightness", "hdr_ui_brightness", Choices.HdrUiBrightness, d: "1", gate: "hdr");
            C("HDR", "UI colour", "hdr_ui_color", Choices.HdrUiColor, d: "1", gate: "hdr");
            T("Info", "Note while shaders build", "compile_note", d: "1");
            T("Info", "Show FPS", "show_fps", d: "1");
            foreach (var n in new[]
            {
                ("Frame rate", "show_fps_rate"),
                ("Frame time", "show_fps_frame_time"),
                ("Upscaler", "show_fps_upscaler"),
                ("CPU usage", "show_fps_cpu"),
                ("GPU usage", "show_fps_gpu")
            }

            )
                T("Info", n.Item1, n.Item2, d: n.Item2 is "show_fps_cpu" or "show_fps_gpu" ? "0" : "1", gate: "show_fps");
            foreach (var n in new[]
            {
                ("VRAM cleanup", "exp_vram_cleanup"),
                ("Precise frame pacing", "exp_frame_pacing"),
                ("Use more VRAM for textures", "exp_vram_budget"),
                ("Graphics threads first", "exp_thread_priority"),
                ("Shorter sleep spin", "exp_sleep_spin"),
                ("Background shader compiling", "exp_async_pipelines"),
                ("Occlusion culling", "exp_occlusion"),
                ("Faster draw thread", "exp_fast_draws"),
                ("No shader freezes", "exp_compile_skip"),
                ("PC memory model", "exp_guest_in_place"),
                ("Native draw calls", "exp_native_calls"),
                ("Faster loading", "exp_fast_loading"),
                ("Hot memory pages", "exp_hot_pages")
            }

            )
                T("Experimental", n.Item1, n.Item2, true, n.Item2 == "exp_occlusion" ? "false" : "true");
            return o;
        }

        public static bool Visible(Option o, ProfileSession s) => o.Visible switch
        {
            "custom" => UpscalerLayout.For(s.Ini.Get("upscaler"), s.Ini.Get("preset")).CustomScaleVisible,
            "dlss" => s.Ini.Get("upscaler") == "dlss",
            _ => o.Section != "DLSS options" || s.Ini.Get("upscaler") == "dlss"
        };
        public static bool Enabled(Option o, ProfileSession s)
        {
            if (o.Gate != null && !s.Ini.Flag(o.Gate))
                return false;
            return o.Key switch
            {
                "preset" => UpscalerLayout.For(s.Ini.Get("upscaler"), s.Ini.Get("preset")).PresetEnabled,
                "sharpen" or "sharpness" or "mip_bias" => s.Ini.Get("upscaler") != "off",
                "frame_gen_cap" => s.Ini.Get("frame_gen", "off") != "off",
                "frame_gen_frames" or "frame_gen_dynamic" or "reflex" => s.Ini.Get("frame_gen", "off") == "dlss",
                _ => true
            };
        }
    }
}
