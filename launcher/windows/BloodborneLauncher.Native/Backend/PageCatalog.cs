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
        "Cheats"
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
                E("", "Saves folder", "user_dir", true);
                E("", "DLC folder", "dlc_dir", true);
                T("", "Enable DLC", "dlc_enabled", true, "true");
                C("Language", "Text language", "text_language", Choices.GameLanguages, true, "1");
                C("Language", "Voice language", "language", Choices.VoiceLanguages, true, "1");
                C("Player", "Player profile", "player_profile", PlayerProfiles.All(p.ReadSettings()).Select(n => Choice.Literal(n, n)).ToArray(), true, "Main");
                A("Player", "New player profile", "player-new");
                T("Saves", "Back up saves before playing", "save_backups", true, "true");
                A("Saves", "Create backup", "backup");
                A("Saves", "Open backups folder", "backups-folder");
                A("Saves", "Restore backup", "restore-backup");
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
                T("", "Camera tuning", "camera_controls");
                N("", "Field of view", "camera_fov_scale", 1, 1.5, .05, gate: "camera_controls");
                N("", "Distance", "camera_distance_scale", .5, 1.5, .05, gate: "camera_controls");
                N("", "Height", "camera_height_scale", .5, 1.5, .05, gate: "camera_controls");
                break;
            case 5:
                T("", "Online", "online_enabled");
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

        return rows;
    }
}
