using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class Upstream115Tests
{
    internal static void Run(ProfileSession profile)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        var graphics = GraphicsSchema.Build();
        foreach (var key in new[] { "effect_gtao", "effect_modern_ssr", "effect_pom_cavity", "effect_contact_shadows", "effect_pcss", "effect_gi", "effect_volumetric_light", "effect_point_fog", "effect_point_shadows", "effect_ray_tracing" })
            Check(graphics.Count(o => o.Key == key && o.Default == "0") == 1, "beta29 effect is exposed once and remains opt-in: " + key);
        foreach (var key in new[] { "graphics_shadow_resolution", "graphics_local_shadow_resolution", "graphics_motion_blur_quality", "graphics_gi_strength", "graphics_ssr_intensity", "graphics_volumetric_intensity", "graphics_point_fog_intensity" })
            Check(graphics.Count(o => o.Key == key && o.Section == "Lighting") == 1, "beta29 lighting control is available: " + key);
        Check(PageCatalog.Build(5, profile).Single(o => o.Key == "coop_open_room").Default == "0", "visitor access is off until the host explicitly enables it");
        var previousConfig = Paths.ConfigDir;
        var previousIni = Paths.IniPath;
        var temp = Path.Combine(Path.GetTempPath(), "better-kbm-looks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Paths.ConfigDir = temp;
            Paths.IniPath = Path.Combine(temp, "lantern.ini");
            var ini = new IniFile();
            ini.Set("key.cross", "E");
            ini.Set("text_language", "20");
            ini.Set("output_res", "2560x1440");
            ini.Set("graphics_brightness", "1.23");
            ini.Set("effect_gi", "0");
            var own = Looks.FromCurrent(ini, "My picture", "Test");
            var parsed = Looks.Parse(Looks.ToCode(own));
            Check(parsed.Name == own.Name && parsed.Values["graphics_brightness"] == "1.23", "visual presets round-trip through the author's share code format");
            Looks.Apply(Looks.Presets.Last(), ini);
            Check(ini.Get("effect_gi") == "1" && ini.Get("key.cross") == "E" && ini.Get("output_res") == "2560x1440" && ini.Get("text_language") == "20", "applying a look changes picture values without changing controls, resolution or language");
            Looks.GoBack(ini);
            Check(ini.Get("graphics_brightness") == "1.23" && ini.Get("effect_gi") == "0" && !Looks.HasPrevious, "go back restores the exact previous picture values");
            Looks.Save(parsed);
            Check(Looks.Saved().Count == 1 && Looks.Saved()[0].Name == own.Name, "saved looks reload from their own configuration folder");
            Looks.Delete(parsed);
            Check(Looks.Saved().Count == 0, "deleting a saved look leaves no stale preset");
            bool rejected = false;
            try { Looks.Parse("{\"lantern_look\":1,\"name\":\"Safe\",\"values\":{\"key.cross\":\"Space\",\"game_dir\":\"other\"}}"); }
            catch (FormatException) { rejected = true; }
            Check(rejected, "imported looks reject device, path and binding data");
            var restricted = Looks.Parse("{\"lantern_look\":1,\"name\":\"Safe\",\"values\":{\"graphics_brightness\":\"99\"}}");
            Check(restricted.Values["graphics_brightness"] == "5", "imported looks clamp picture values to the engine's valid range");
        }
        finally
        {
            Paths.ConfigDir = previousConfig;
            Paths.IniPath = previousIni;
            Directory.Delete(temp, true);
        }
    }
}
