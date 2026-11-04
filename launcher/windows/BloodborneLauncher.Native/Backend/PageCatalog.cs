using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;
/// <summary>Native page descriptions; upstream services own profiles, validation and file formats.</summary>
internal static class PageCatalog
{
    public static readonly string[] Titles =
    {
        "Start",
        "Graphics",
        "Game",
        "Controls",
        "Tweaks",
        "Online",
        "Achievements",
        "Settings",
        "More",
        "Cheats",
        "Saves"
    };
    public static IReadOnlyList<Option> Build(int page, ProfileSession p, string? inputDevice = null)
    {
        var rows = new List<Option>();
        void T(string s, string t, string k, bool setting = false, string d = "0", string? gate = null) => rows.Add(new(s, t, k, OptionKind.Toggle, setting, Default: d, Gate: gate));
        void C(string s, string t, string k, IReadOnlyList<Choice> c, bool setting = false, string d = "0") => rows.Add(new(s, t, k, OptionKind.Choice, setting, c, Default: d));
        void N(string s, string t, string k, double min, double max, double step, string d = "1", string? gate = null, bool setting = false) => rows.Add(new(s, t, k, OptionKind.Slider, setting, Min: min, Max: max, Step: step, Default: d, Gate: gate));
        void E(string s, string t, string k, bool folder = false, bool secret = false, bool setting = true) => rows.Add(new(s, t, k, folder ? OptionKind.Folder : secret ? OptionKind.Password : OptionKind.Text, setting, Default: ""));
        void A(string s, string t, string key) => rows.Add(new(s, t, "action:" + key, OptionKind.Action));
        switch (page)
        {
            case 2:
                E("", "Game folder", "game_dir", true);
                E("", "Engine folder", "port_dir", true);
                E("", "DLC folder", "dlc_dir", true);
                T("", "Enable DLC", "dlc_enabled", true, "true");
                C("Language", "Text language", "text_language", Choices.GameLanguages, true, "1");
                C("Language", "Voice language", "language", Choices.VoiceLanguages, true, "1");
                T("Backups", "Back up saves before playing", "save_backups", true, "true");
                E("Other", "Saves folder", "user_dir", true);
                A("Profiles", "New profile", "save-profile-new");
                A("Backups", "Create backup", "save-backup-create");
                A("Backups", "Open backups folder", "save-backup-folder");
                A("Other", "Import saves", "save-import");
                break;
            case 3:
                string device = inputDevice ?? p.InputDevice;
                C("", "Profile", "native-profile", ControlProfiles.All(device).Select(x => Choice.Literal(x.Name, x.Name)).ToArray(), true, p.Str("controls_profile." + device, device == "keyboard_mouse" ? ControlProfiles.DarkSouls3Name : "Controller"));
                foreach (var pair in new[]
                {
                    ("Save as", "save"),
                    ("Copy JSON", "copy"),
                    ("Export file", "export"),
                    ("Import file", "import"),
                    ("Import from link", "link"),
                    ("Delete this profile", "delete")
                }

                )
                    A("", pair.Item1, "profile-" + pair.Item2);
                if (device == "keyboard_mouse")
                {
                    T("", "Mouse camera", "mouse_camera", d: "1");
                    T("", "Mouse in menus", "mouse_menu", d: "1");
                    N("", "Mouse sensitivity", "mouse_sensitivity", -10, 20, 1, "5");
                    T("", "Invert horizontal", "mouse_invert_x");
                    T("", "Invert vertical", "mouse_invert_y");
                    T("", "Camera auto-rotation", "mouse_auto_rotation");
                    C("", "Button prompts", "button_prompts", Choices.ButtonPrompts, d: "auto");
                    T("", "Hide mouse cursor", "hide_cursor", d: "1");
                    rows.Add(new("Movement", "Turning speed", "stick_turn_rate", OptionKind.Slider, Min: 0, Max: 5000, Step: 50, Default: "800", Hint: MovementHints.Turning));
                    T("Movement", "Circular stick range", "stick_circular", d: "1");
                }
                else
                {
                    C("", "Controller icons", "pad_icons", Choices.PadIcons, d: "auto");
                    T("", "Swap sticks", "pad_swap_sticks");
                    T("", "Invert camera left / right", "pad_invert_look_x");
                    T("", "Invert camera up / down", "pad_invert_look_y");
                    N("", "Stick dead zone", "pad_deadzone", 0, 30, 1, "0");
                }

                break;
            case 4:
                T("Gameplay Tweaks", "Hunter's dream mirror opens the appearance editor", "change_appearance");
                T("Gameplay Tweaks", "The Altar of Despair resets level and attributes with the Yharnam Stone", "rebirth");
                foreach (var e in Choices.Effects.Where(e => e.Key is "skip_intro" or "debug_camera"))
                    T("Gameplay Tweaks", e.SourceTitle, e.Key);
                rows.Add(new("", "Camera tuning", "camera_controls", OptionKind.Toggle)
                    { Heading = true, Icon = "\uE722" });
                N("", "Field of view", "camera_fov_scale", 1, 1.5, .05, gate: "camera_controls");
                N("", "Distance", "camera_distance_scale", .5, 1.5, .05, gate: "camera_controls");
                N("", "Height", "camera_height_scale", .5, 1.5, .05, gate: "camera_controls");
                break;
            case 5:
                T("", "Online", "online_enabled");
                C("", "Server", "online_server", Choices.OnlineServers, d: "huntersdream");
                T("Seamless co-op", "Seamless co-op", "seamless_enabled");
                rows.Add(new("Seamless co-op", "Open my room to visitors", "coop_open_room", OptionKind.Toggle, Default: "0",
                    Hint: "While on, your game gives your room's invite code to Lantern's co-op server, and the Lantern event team can see it in Discord and join for surprise events, so your room is not private from them. Everyone in the room sees a line when a visitor joins, and you can Kick them. Nobody is moved: guests keep control of their own summons. Off or leaving takes the room off the list at once; New room retires a code they already saw"));
                C("Seamless co-op", "Role", "seamless_role", Choices.SeamlessRoles, d: "guest");
                C("Seamless co-op", "Connection", "seamless_direct", new[] { new Choice("Server", "0"), new Choice("Direct P2P", "1") });
                E("Seamless co-op", "Your address for guests", "seamless_direct_address", setting: false);
                E("Seamless co-op", "Direct port", "seamless_direct_port", setting: false);
                E("Seamless co-op", "Host's address", "seamless_direct_host", setting: false);
                E("Seamless co-op", "Plate name", "seamless_player", setting: false);
                E("Seamless co-op", "Host title", "plate_host_title", setting: false);
                E("Seamless co-op", "Guest title", "plate_guest_title", setting: false);
                A("Seamless co-op", "Copy invite code", "room-copy");
                A("Seamless co-op", "Paste invite code", "room-paste");
                A("Seamless co-op", "New room", "room-new");
                T("Co-op interface", "Connection status", "coop_status", d: "1");
                T("Co-op interface", "Ping on party portraits", "coop_hud_ping", d: "1");
                T("Co-op interface", "Party notices", "coop_notices", d: "1");
                T("Co-op interface", "Warp requests", "coop_warp_requests", d: "1");
                T("Voice chat", "Voice chat", "voice_enabled", d: "1");
                T("Voice chat", "Push to talk", "voice_push_to_talk", d: "1", gate: "voice_enabled");
                T("Voice chat", "Mute microphone", "voice_mic_muted", gate: "voice_enabled");
                C("Voice chat", "Talk key", "voice_talk_key", new[] { new Choice("V", "25"), new Choice("B", "5"), new Choice("Left Alt", "226"), new Choice("Mouse 4", "-4"), new Choice("Mouse 5", "-5") }, d: "25");
                N("Voice chat", "Voice volume", "voice_volume", 0, 5, .05, "1", gate: "voice_enabled");
                A("Hunter's Dream account", p.Str("hd_name", "Connect"), "online-link");
                A("Hunter's Dream account", "Create account", "online-create");
                A("Hunter's Dream account", "Recover account", "online-recover");
                E("shadNet account", "Username", "online_username");
                E("shadNet account", "Password", "online_password", secret: true);
                T("Connection", "UPnP", "online_upnp", d: "1");
                E("Connection", "Local P2P port", "online_p2p_port", setting: false);
                C("Connection", "Summon sign timeout", "online_sign_timeout", Choices.SignTimeout, d: "30");
                A("Connection", "Test the connection", "online-test");
                break;
            case 6:
                T("Notification", "Show in game", "trophy_notifications", true, "true");
                T("Notification", "Play a sound", "trophy_sound", true, "true");
                N("Notification", "Volume", "trophy_volume", 0, 100, 5, "50", setting: true);
                C("Notification", "Position", "trophy_position", new[] { new Choice("Top right", "top-right"), new Choice("Top left", "top-left"), new Choice("Bottom right", "bottom-right"), new Choice("Bottom left", "bottom-left") }, true, "top-right");
                break;
            case 7:
                C("Launch", "Start the game with", "launch_method", Choices.LaunchMethods, true, "windows");
                E("Launch", "Custom command", "custom_command");
                E("Launch", "Custom arguments", "custom_args");
                C("Performance", "Two-stage GPU pipeline", "draw_pipe", Choices.DrawPipe, true, "");
                C("Performance", "CPU reads of GPU data", "readbacks", Choices.Readbacks, true, "");
                T("Performance", "Mute when unfocused", "mute_unfocused", true, "false");
                C("Performance", "Sound output", "sound_output", Choices.SoundOutput, true, "");
                T("Performance", "Sound threads first", "audio_priority", true, "true");
                foreach (var e in new[]
                {
                    ("Frame statistics in the log", "frame_stats"),
                    ("FSR 4 profile in the log", "fsr_profile"),
                    ("Audio statistics in the log", "audio_stats"),
                    ("Loading profile in the log", "load_profile"),
                    ("Render stage timings in the log", "section_stats")
                }

                )
                    T("Diagnostics", e.Item1, e.Item2, true, "false");
                C("Discord", "Discord status", "discord_status", new[] { new Choice("Off", "off"), new Choice("Lantern Engine", "lantern") }, true, "off");
                T("Discord", "Show current location", "discord_game_place", true, "true");
                T("Discord", "Show character statistics", "discord_game_stats", true, "true");
                E("Diagnostics", "Extra variables", "extra_env");
                foreach (var e in new[]
                {
                    ("Launcher settings", "config"),
                    ("Port settings (bbport.ini)", "ini"),
                    ("Share your settings", "share"),
                    ("Import your settings", "import"),
                    ("Shader cache", "cache")
                }

                )
                    A("Files", e.Item1, "settings-" + e.Item2);
                break;
            case 8:
                E("Mods", "Mods folder", "mods_dir", true);
                T("Mods", "Enable mods", "mods_enabled", true, "true");
                A("Mods", "Mods", "mods");
                E("Patches", "Patches folder", "patches_dir", true);
                A("Patches", "Third-party patches", "patches");
                A("Logs", "Open logs folder", "logs");
                A("Logs", "View log", "view-log");
                A("Engine", "Engine", "engine");
                break;
            case 9:
                foreach (var e in new[]
                {
                    ("Blood Vials", "cheat_vials"),
                    ("Quicksilver Bullets", "cheat_bullets")
                }

                )
                {
                    C("Items", e.Item1, e.Item2, new[] { new Choice("Off", "off"), new Choice("Infinite", "infinite"), new Choice("Custom", "custom") }, d: "off");
                    if (int.TryParse(p.Ini.Get(e.Item2), out int n) && n > 0)
                        N("Items", "Amount", e.Item2 + "_amount", 1, 99, 1, "20", setting: true);
                }

                T("Items", "Infinite consumables", "cheat_consumables");
                T("Character", "Infinite stamina", "cheat_stamina");
                T("Character", "Stealth", "cheat_stealth");
                T("Character", "No weapon wear", "cheat_no_durability");
                C("Character", "Firearm range", "cheat_gun_range", new[] { new Choice("Off", "off"), new Choice("150%", "150"), new Choice("200%", "200"), new Choice("300%", "300") }, d: "off");
                T("Blood Echoes", "Keep echoes on death", "cheat_keep_echoes");
                C("Blood Echoes", "Echoes from kills", "cheat_echo_multiplier", new[] { "1", "2", "5", "10" }.Select(n => Choice.Literal(n + "×", n)).ToArray(), d: "1");
                break;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            string? hint = rows[i].Key switch
            {
                "seamless_enabled" => "Play the campaign together in your own encrypted room: no Hunter's Dream, no ports to open",
                "seamless_role" => "One player hosts, the others join the host's world",
                "seamless_player" => "Your name on the plate above your hunter and in the party list (up to 16 characters, spaces and symbols allowed)",
                "plate_host_title" => "The line above the host's name on plates in your game. Empty: only the name. The game's own is Beckoner",
                "plate_guest_title" => "The line above each guest's name on plates in your game. Empty: only the name. The game's own is Cooperator",
                "online_p2p_port" => "Empty: 3658, the PS4's",
                "seamless_direct" => "Server uses Lantern's relay. Direct P2P connects guests directly to the host over a reachable network or forwarded port.",
                "seamless_direct_address" => "Your VPN or public address for guests. Empty uses an address on this network.",
                "seamless_direct_port" => "Direct room TCP port. Default: 38765.",
                "seamless_direct_host" => "The host's address or address:port, normally filled by the invite code.",
                _ => null
            };
            if (hint != null) rows[i] = rows[i] with { Hint = hint };
        }
        return rows;
    }
}
