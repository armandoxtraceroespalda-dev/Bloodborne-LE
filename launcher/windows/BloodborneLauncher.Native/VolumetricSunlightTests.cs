using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class VolumetricSunlightTests
{
    internal static void Run(ProfileSession profile)
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        const string variable = "BB_VOLUMETRIC_STEADY";
        const string key = "env:" + variable;
        Check(!EngineSwitches.All.Any(s => s.Env == variable), "sunlight correction has no independent switch");
        Check(!GraphicsSchema.Build().Any(row => row.Key == key), "sunlight correction is absent from Graphics");
        string? effect = profile.Ini.Raw("effect_volumetric_light"), saved = profile.Str(key);
        try
        {
            profile.Set(key, "0", true);
            Check(!EngineSwitches.Changed(profile.SourceSettings()).Any(pair => pair.Name == variable), "old disabled sunlight setting is not forwarded");
            Check(!SettingsSearchIndex.Build(profile, false).Find(key).Any(), "removed sunlight switch is absent from search");
        }
        finally
        {
            profile.Ini.Set("effect_volumetric_light", effect);
            profile.Set(key, string.IsNullOrEmpty(saved) ? null : saved, true);
            UiText.UseTextLanguage("1");
        }
    }
}
