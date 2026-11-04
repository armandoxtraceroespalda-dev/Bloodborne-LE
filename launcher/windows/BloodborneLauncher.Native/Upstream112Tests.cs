using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class Upstream112Tests
{
    internal static void Run(ProfileSession profile)
    {
        static void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException(label);
            Console.WriteLine("PASS " + label);
        }
        var game = PageCatalog.Build(2, profile);
        foreach (string key in new[] { "save_backups", "user_dir", "action:save-profile-new", "action:save-backup-create", "action:save-backup-folder", "action:save-import" })
            Check(game.Count(o => o.Key == key) == 1, "Game exposes its save management option once: " + key);
        var index = SettingsSearchIndex.Build(profile, false);
        Check(index.Find("save_backups").Any(result => result.Page == 2 && result.Key == "save_backups"),
            "save management search results navigate to Game");
        var online = PageCatalog.Build(5, profile);
        foreach (string key in new[] { "seamless_direct", "seamless_direct_address", "seamless_direct_port", "seamless_direct_host", "coop_status", "coop_hud_ping", "coop_notices", "coop_warp_requests" })
            Check(online.Any(o => o.Key == key), "native Online exposes " + key);
        Check(online.Single(o => o.Key == "seamless_direct").Choices!.Select(c => c.Value).SequenceEqual(new[] { "0", "1" }),
            "direct connection uses the engine's existing boolean INI contract");
        foreach (string key in EngineSwitches.NativeSwitches)
            Check(EngineSwitches.All.Any(s => s.Env == key), "native renderer switch is configurable: " + key);
        string[] keys = { "text_language", "language", "future_setting", "mouse_sensitivity" };
        var original = keys.ToDictionary(k => k, k => profile.Ini.Raw(k));
        var settings = profile.SourceSettings();
        string previousConfig = Paths.ConfigDir;
        string? backupFile = null;
        try
        {
            Paths.ConfigDir = Path.Combine(Path.GetTempPath(), "better-kbm-tested-" + Guid.NewGuid().ToString("N"));
            settings.Set("exp_fast_draws", false);
            profile.Ini.Set("effect_dynamic_shadows", "1");
            profile.Ini.Set("future_setting", "retain");
            backupFile = TestedSettings.Use(profile.Ini, settings);
            Check(File.Exists(backupFile) && settings.Bool("exp_fast_draws") && profile.Ini.Get("effect_dynamic_shadows") == "0", "tested settings back up and apply the upstream values");
            Check(profile.Ini.Get("future_setting") == "retain", "tested settings preserve unrelated and future settings");
            TestedSettings.Restore(profile.Ini, settings);
            Check(!settings.Bool("exp_fast_draws") && profile.Ini.Get("effect_dynamic_shadows") == "1", "tested settings restore the player's previous values");
            Check(SaveStore.DefaultCharacter(Array.Empty<SaveCharacter>(), "Main") == -1, "an empty saves directory is supported");
        }
        finally
        {
            profile.Ini.Set("effect_dynamic_shadows", null);
            foreach (var pair in original) profile.Ini.Set(pair.Key, pair.Value);
            if (Directory.Exists(Paths.ConfigDir)) Directory.Delete(Paths.ConfigDir, true);
            Paths.ConfigDir = previousConfig;
        }
        foreach (string language in new[] { "0", "2", "3", "4", "5", "8", "17", "20" })
        {
            UiText.UseTextLanguage(language);
            Check(UiText.T("Saves") != "Saves" && UiText.T("Save editor") != "Save editor", "new save screens follow text language " + language);
        }
        UiText.UseTextLanguage("1");
    }
}
