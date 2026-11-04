using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;

namespace BloodborneLauncher.Native;
internal static class GraphicsProfilesTests
{
    internal static void Run()
    {
        static void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        string temp = Path.Combine(Path.GetTempPath(), "graphics-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previous = (Paths.IniPath, Paths.ConfigDir, Paths.ConfigFile, Paths.DataDir, Paths.PortDir, Workspace.Current.Ini);
        try
        {
            string config = Path.Combine(temp, "settings.json"), ini = Path.Combine(temp, "lantern.ini"), staged = Path.Combine(temp, "profile");
            File.WriteAllText(config, "{\"text_language\":\"1\"}");
            File.WriteAllText(ini, "graphics_brightness=1.23\noutput_res=2560x1440\nkey.cross=E\nvoice_language=17\n");
            string[] args = { "--config", config, "--ini", ini, "--assets", temp, "--profile", staged };
            var profile = new ProfileSession(args);
            Check(GraphicsProfiles.Selection(profile) == GraphicsProfiles.Custom, "the initial graphics profile is Custom");
            Check(GraphicsProfiles.Apply(profile, "builtin:0"), "the first visual profile can be applied");
            GraphicsProfiles.Apply(profile, "builtin:3");
            Check(profile.Ini.Flag("effect_gi"), "a second preset changes visual values");
            profile.SavePreview();
            profile.ApplyToGame();
            profile = new ProfileSession(args);
            Check(GraphicsProfiles.Selection(profile) == "builtin:3", "selected profile survives a launcher restart");
            GraphicsProfiles.Apply(profile, GraphicsProfiles.Custom);
            Check(profile.Ini.Raw("graphics_brightness") == "1.23" && profile.Ini.Raw("effect_gi") == "0" && profile.Ini.Raw("graphics_hue") == null,
                "Custom survives repeated selections and restart, including absent INI values");
            Check(profile.Ini.Get("key.cross") == "E" && profile.Ini.Get("output_res") == "2560x1440" && profile.Ini.Get("voice_language") == "17",
                "restoring Custom preserves controls, output resolution and language");
            profile.WriteOption(new("Picture", "Brightness", "graphics_brightness", OptionKind.Slider), "1.42");
            GraphicsProfiles.Apply(profile, "builtin:1");
            GraphicsProfiles.Apply(profile, GraphicsProfiles.Custom);
            Check(profile.Ini.Get("graphics_brightness") == "1.42", "edited Custom settings become the next recoverable snapshot");
            var saved = Looks.FromCurrent(profile.Ini, "Z profile", "");
            Looks.Save(saved);
            string id = GraphicsProfiles.Id(saved);
            GraphicsProfiles.Apply(profile, id);
            var earlier = Looks.FromCurrent(profile.Ini, "A profile", "");
            Looks.Save(earlier);
            Check(GraphicsProfiles.Selection(profile) == id, "adding an alphabetically earlier profile cannot change the selected profile identity");
            Looks.Delete(earlier);
            Check(GraphicsProfiles.Selection(profile) == id, "deleting another saved profile does not shift the selection");
            var replacement = new Looks.Look("Z profile", "", new Dictionary<string, string> { ["graphics_brightness"] = "1.9" });
            Looks.Save(replacement);
            GraphicsProfiles.Apply(profile, id, preserveCustom: true);
            GraphicsProfiles.Apply(profile, GraphicsProfiles.Custom);
            Check(profile.Ini.Get("graphics_brightness") == "1.42", "replacing an imported active profile cannot overwrite the saved Custom values");
            profile.WriteOption(new("Lighting", "Indirect lighting strength", "graphics_gi_strength", OptionKind.Slider), "7");
            Check(GraphicsProfiles.Selection(profile) == GraphicsProfiles.Custom, "editing a visual value marks the current settings as Custom");
            var choices = GraphicsProfiles.Choices();
            Check(choices.All(choice => !string.IsNullOrWhiteSpace(choice.Note)), "every built-in, Custom and saved profile carries a tooltip");
            var englishNotes = choices.Take(5).Select(choice => choice.Note).ToArray();
            foreach (string language in new[] { "3", "2", "4", "5", "17", "8", "0" })
            {
                UiText.UseTextLanguage(language);
                Check(GraphicsProfiles.Choices().Take(5).Select((choice, i) => choice.Note != englishNotes[i]).All(translated => translated), "profile descriptions follow the selected language " + language);
            }
            UiText.UseTextLanguage("1");
            var graphics = GraphicsSchema.Build();
            var rows = graphics.Where(option => option.Section == "Graphics profiles").ToArray();
            Check(rows.Count(option => option.Kind == OptionKind.Choice) == 1 && rows.Count(option => option.Kind == OptionKind.Action) == 6,
                "graphics profiles use one selector and six compact actions");
            Check(!rows.Any(option => option.Key == "action:look-back") && rows.Single(option => option.Key == "action:look-save").Title == "Save current" && rows.Single(option => option.Key == "action:look-delete").Title == "Delete profile",
                "profile actions use the requested names without Go back");
            foreach (string key in new[] { "frame_gen", "frame_gen_cap", "frame_gen_frames", "frame_gen_dynamic", "reflex" })
                Check(graphics.Single(option => option.Key == key).Section == "Frame generation", "frame generation section owns " + key);
            profile.Ini.Set("frame_gen", "off");
            foreach (string key in new[] { "frame_gen_cap", "frame_gen_frames", "frame_gen_dynamic", "reflex" })
            {
                var dependent = graphics.Single(option => option.Key == key);
                Check(GraphicsSchema.Visible(dependent, profile) && !GraphicsSchema.Enabled(dependent, profile), "frame generation option stays visible and disabled while off: " + key);
            }
            profile.Ini.Set("frame_gen", "dlss");
            Check(GraphicsSchema.Visible(graphics.Single(option => option.Key == "frame_gen_frames"), profile), "DLSS frame count appears with DLSS generation");
            foreach (float width in new[] { 480f, 600f, 820f, 1100f, 1800f })
            {
                var row = GraphicsProfileLayout.Place(width);
                Check((row.Stacked || row.ActionsX > row.SelectorWidth) && row.ActionsX + 3 * row.ButtonWidth + 2 * row.Gap <= width && row.ButtonWidth > 80,
                    "two-row labeled profile layout fits without overlap at width " + width);
            }
            profile.EditSource((settings, input) => Looks.Apply(Looks.Presets[0], input));
            profile.Set("native-look", "0", true);
            File.Delete(Path.Combine(staged, "graphics-custom.json"));
            GraphicsProfiles.Apply(profile, "builtin:3");
            GraphicsProfiles.Apply(profile, GraphicsProfiles.Custom);
            Check(profile.Ini.Get("graphics_brightness") == "1.23", "upgrading from version 115 recovers the original Custom snapshot");
        }
        finally
        {
            Paths.IniPath = previous.Item1; Paths.ConfigDir = previous.Item2; Paths.ConfigFile = previous.Item3;
            Paths.DataDir = previous.Item4; Paths.EngineDir = previous.Item5; Workspace.Current.Ini = previous.Item6;
            Directory.Delete(temp, true);
        }
    }
}
