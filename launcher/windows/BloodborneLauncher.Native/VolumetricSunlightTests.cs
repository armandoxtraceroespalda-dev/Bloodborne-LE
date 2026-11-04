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
        var item = EngineSwitches.All.Single(s => s.Env == variable);
        Check(item.Kind == SwitchKind.Toggle && item.Default == item.On && item.Group == EngineSwitches.Graphics,
            "steady sunlight color is an enabled-by-default graphics engine switch");
        var rows = GraphicsSchema.Build().Where(row => row.Key == item.Key).ToArray();
        Check(rows.Length == 1 && rows[0].Section == "Lighting" && rows[0].Setting && rows[0].Gate == "effect_volumetric_light",
            "the native Graphics Lighting section exposes the fix exactly once next to volumetric sunlight");
        var row = rows[0];
        string? effect = profile.Ini.Raw("effect_volumetric_light"), saved = profile.Str(item.Key);
        try
        {
            profile.Ini.Set("effect_volumetric_light", "0");
            Check(GraphicsSchema.Visible(row, profile) && !GraphicsSchema.Enabled(row, profile),
                "steady sunlight color remains visible but disabled while volumetric sunlight is off");
            profile.Ini.Set("effect_volumetric_light", "1");
            Check(GraphicsSchema.Enabled(row, profile), "steady sunlight color becomes selectable with volumetric sunlight");
            profile.WriteOption(row, "0");
            Check(EngineSwitches.Changed(profile.SourceSettings()).Any(pair => pair.Name == variable && pair.Value == "0"),
                "turning the native switch off sends BB_VOLUMETRIC_STEADY=0 to the engine contract");
            profile.WriteOption(row, "1");
            Check(!EngineSwitches.Changed(profile.SourceSettings()).Any(pair => pair.Name == variable),
                "turning the native switch on restores the engine's enabled default");
            Check(!Looks.Knobs.Any(knob => knob.Key == variable || knob.Key == item.Key) && !Choices.Effects.Any(effect => effect.Key == variable),
                "graphics profiles and effect presets do not override the steady sunlight switch");
            Check(SettingsSearchIndex.Build(profile, false).Find(item.Key).Any(result => result.Page == 1),
                "the global settings search locates steady sunlight color in Graphics");
            foreach (string language in new[] { "0", "2", "3", "20", "4", "5", "8", "17" })
            {
                UiText.UseTextLanguage(language);
                Check(item.Title != item.SourceTitle && UiText.T(item.Hint) != item.Hint && EngineSwitches.Describe(item).Contains(UiText.T(item.Hint)),
                    "steady sunlight title and tooltip follow game text language " + language);
            }
        }
        finally
        {
            profile.Ini.Set("effect_volumetric_light", effect);
            profile.Set(item.Key, string.IsNullOrEmpty(saved) ? null : saved, true);
            UiText.UseTextLanguage("1");
        }
    }
}
