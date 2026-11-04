using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;

internal static class Upstream104Tests
{
    internal static void Run(ProfileSession profile)
    {
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }
        var online = PageCatalog.Build(5, profile);
        Check(online.Select(o => o.Key).Distinct().Count() == online.Count,
            "updated online schema has unique keys");
        foreach (string key in new[] { "seamless_enabled", "seamless_role", "seamless_player", "plate_host_title", "plate_guest_title", "voice_enabled", "voice_talk_key", "voice_volume" })
            Check(online.Any(o => o.Key == key), "native online exposes " + key);
        if (OperatingSystem.IsWindows())
        {
        profile.WriteOption(online.Single(o => o.Key == "seamless_role"), "host");
        string room = profile.Ini.Get("seamless_host_room");
        string password = profile.ReadSettings().SeamlessHostPassword;
        Check(room.Length == 20 && password.Length == 24,
            "selecting Host creates a usable private room without copying an invite first");
        profile.WriteOption(online.Single(o => o.Key == "seamless_role"), "host");
        Check(room == profile.Ini.Get("seamless_host_room") && password == profile.ReadSettings().SeamlessHostPassword,
            "reselecting Host preserves its existing invite");
        Check(!profile.Ini.Values.Any(kv => kv.Value == password),
            "room secrets stay in protected launcher settings, outside engine INI");
        }
        else Console.WriteLine("SKIP protected room-secret roundtrip requires Windows DPAPI");
        profile.WriteOption(online.Single(o => o.Key == "seamless_enabled"), "1");
        Check(profile.Ini.Get("online_enabled") == "1", "enabling seamless co-op also enables online play");
        var server = online.Single(o => o.Key == "online_server");
        profile.WriteOption(server, SuperMedo.Id);
        Check(profile.Ini.Get("online_host") == SuperMedo.Host && profile.Value(server) == SuperMedo.Id,
            "server selector applies SuperMedo endpoints");
        profile.WriteOption(server, "huntersdream");
        Check(profile.Ini.Get("online_host") == "" && profile.Value(server) == "huntersdream",
            "returning to Hunter's Dream clears the other provider endpoints");
        Check(online.Single(o => o.Key == "voice_enabled").Default == "1" &&
            online.Single(o => o.Key == "voice_push_to_talk").Default == "1",
            "voice toggle defaults match the engine's enabled push-to-talk defaults");
        var graphics = GraphicsSchema.Build();
        Check(graphics.Any(o => o.Key == "ui_4k") && graphics.Any(o => o.Key == "action:ui4k-build"),
            "native graphics exposes local 4K menu generation");
        Check(graphics.Single(o => o.Key == "ui_4k").Default == "1",
            "a completed 4K pack uses the same enabled default in the UI and starter");
        Check(GraphicsSchema.Enabled(graphics.Single(o => o.Key == "ui_4k"), profile),
            "4K menu switch can initiate local generation before a texture pack exists");
        Check(profile.Value(graphics.Single(o => o.Key == "ui_4k")) != "1" || UiUpscale.Ready,
            "an unbuilt texture pack never appears enabled");
        Check(PageCatalog.Build(7, profile).Any(o => o.Key == "mute_unfocused"),
            "native settings exposes background sound muting");
        Check(Choices.OnlineServers.Any(c => c.Value == SuperMedo.Id),
            "server choices include SuperMedo");
        profile.Set("seamless_player", "Saved player name");
        profile.Set("seamless_enabled", "0");
        var seamless = online.Where(o => o.Section == "Seamless co-op").ToArray();
        Check(GraphicsSchema.Enabled(seamless.Single(o => o.Key == "seamless_enabled"), profile),
            "seamless switch remains available while the feature is off");
        Check(seamless.Where(o => o.Key != "seamless_enabled").All(o => !GraphicsSchema.Enabled(o, profile)),
            "all seamless fields and invite actions are disabled when seamless is off");
        Check(GraphicsSchema.Enabled(online.Single(o => o.Key == "online_server"), profile),
            "disabling seamless does not disable the general server selector");
        profile.Set("seamless_enabled", "1");
        Check(seamless.All(o => GraphicsSchema.Enabled(o, profile)),
            "seamless fields and invite actions reactivate with the switch");
        Check(profile.Ini.Get("seamless_player") == "Saved player name",
            "disabling and reenabling seamless preserves its saved settings");
        var seamlessToggle = online.Single(o => o.Key == "seamless_enabled");
        var onlineToggle = online.Single(o => o.Key == "online_enabled");
        profile.Set("seamless_role", "guest");
        profile.Set("seamless_enabled", "0");
        profile.Set("online_enabled", "0");
        profile.WriteOption(seamlessToggle, "1");
        Check(profile.Ini.Get("online_enabled") == "1" && profile.Str("seamless_restore_offline") == "true",
            "enabling seamless records that online was previously off");
        profile.WriteOption(seamlessToggle, "1");
        Check(profile.Str("seamless_restore_offline") == "true",
            "repeated seamless enable preserves the original online preference");
        profile.WriteOption(seamlessToggle, "0");
        Check(profile.Ini.Get("online_enabled") == "0" && profile.Str("seamless_restore_offline") == "",
            "disabling seamless restores offline when online was enabled only for seamless");
        profile.WriteOption(onlineToggle, "1");
        profile.WriteOption(seamlessToggle, "1");
        profile.WriteOption(seamlessToggle, "0");
        Check(profile.Ini.Get("online_enabled") == "1",
            "disabling seamless preserves independently enabled online play");
        profile.WriteOption(onlineToggle, "0");
        profile.WriteOption(seamlessToggle, "1");
        profile.WriteOption(onlineToggle, "1");
        profile.WriteOption(seamlessToggle, "0");
        Check(profile.Ini.Get("online_enabled") == "1",
            "an explicit online choice supersedes temporary seamless activation");
        var cameraToggle = PageCatalog.Build(4, profile).Single(o => o.Key == "camera_controls");
        Check(cameraToggle.Heading && cameraToggle.Icon.Length == 1 && cameraToggle.Kind == OptionKind.Toggle,
            "Camera tuning is one heading with its own icon and switch");
        profile.WriteOption(onlineToggle, "0");
        profile.WriteOption(seamlessToggle, "1");
        profile.SavePreview();
        string stagedPath = Path.Combine(Path.GetDirectoryName(profile.SandboxIni)!, "staged.json");
        var staged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(stagedPath))!;
        Check(staged["ini"]?["online_enabled"]?.ToString() == "1" &&
            staged["ini"]?["seamless_enabled"]?.ToString() == "1" &&
            staged["settings"]?["seamless_restore_offline"]?.ToString() == "true",
            "seamless dependency and previous online state are staged for persistence, not only shown in the UI");
        profile.WriteOption(seamlessToggle, "0");
        profile.SavePreview();
        staged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(stagedPath))!;
        Check(staged["ini"]?["online_enabled"]?.ToString() == "0" &&
            staged["ini"]?["seamless_enabled"]?.ToString() == "0",
            "restoring offline is also staged for the next game launch");
    }
}
