using System.Text.Json.Nodes;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class ControlDefaultsTests
{
    public static void Run(string parent)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        string oldIni = Paths.IniPath, oldConfig = Paths.ConfigFile, oldData = Paths.DataDir, oldEngine = Paths.PortDir, oldConfigDir = Paths.ConfigDir;
        try
        {
            string root = Path.Combine(parent, "device-reset-tests");
            Directory.CreateDirectory(root);
            string ini = Path.Combine(root,"lantern.ini"), json = Path.Combine(root,"settings.json");
            File.WriteAllText(ini,"input_mode=keyboard_mouse\nfuture_key=keep\n");
            File.WriteAllText(json,"{}");
            var profile = new ProfileSession(new[] { "--ini",ini,"--config",json,"--assets",root,"--profile",Path.Combine(root,"staging") });
            profile.Set("mouse_sensitivity","19"); profile.Set("stick_turn_rate","2300");
            profile.Set("pad_deadzone","17"); profile.Set("pad_icons","xbox");
            profile.EditSource((settings, input) => { input.Set("bind.attack","P"); PadMap.Assign(input,"cross","y"); });
            string customPad = string.Join(",",PadMap.Get(profile.Ini,"cross"));
            ControlDefaults.Reset(profile,"keyboard_mouse",false);
            Check(Bindings.Get(profile.Ini,InputMap.Controls.First(value=>value.Name=="attack")).SequenceEqual(new[]{"Mouse1"}),
                "keyboard-only binding reset restores the DS3 layout");
            Check(profile.Ini.Get("mouse_sensitivity")=="19" && profile.Ini.Get("stick_turn_rate")=="2300" &&
                string.Join(",",PadMap.Get(profile.Ini,"cross"))==customPad,
                "binding-only reset preserves tuning and the custom gamepad map");
            ControlDefaults.Reset(profile,"keyboard_mouse",true);
            Check(profile.Ini.Get("stick_turn_rate")=="800" && profile.Ini.Get("mouse_sensitivity")=="5" && profile.InputDevice=="keyboard_mouse",
                "full keyboard reset restores movement and mouse defaults for keyboard mode");
            Check(profile.Ini.Get("pad_deadzone")=="17" && profile.Ini.Get("pad_icons")=="xbox" &&
                string.Join(",",PadMap.Get(profile.Ini,"cross"))==customPad,
                "full keyboard reset preserves gamepad settings and assignments");
            profile.Input("controller");
            profile.EditSource((settings, input)=>input.Set("bind.attack","P"));
            profile.Set("mouse_sensitivity","18"); profile.Set("stick_turn_rate","2400");
            ControlDefaults.Reset(profile,"controller",false);
            Check(PadMap.IsDefault(profile.Ini,"cross") && profile.Ini.Get("pad_deadzone")=="17" && profile.Ini.Get("bind.attack")=="P",
                "gamepad-only binding reset preserves keyboard bindings and gamepad tuning");
            ControlDefaults.Reset(profile,"controller",true);
            Check(profile.Ini.Get("pad_deadzone")=="0" && profile.Ini.Get("pad_icons")=="auto" && profile.InputDevice=="controller" && profile.Ini.Get("mouse_camera")=="0",
                "full gamepad reset restores gamepad defaults without switching to keyboard mode");
            Check(profile.Ini.Get("bind.attack")=="P" && profile.Ini.Get("mouse_sensitivity")=="18" && profile.Ini.Get("stick_turn_rate")=="2400" && profile.Ini.Get("future_key")=="keep",
                "full gamepad reset preserves keyboard tuning, mappings and unknown settings");
            bool rejected = false;
            try { ControlDefaults.Reset(profile,"keyboard_mouse",true); } catch (InvalidOperationException) { rejected=true; }
            Check(rejected,"a stale confirmation cannot reset the wrong input device");

            var sound = EngineSwitches.All.First(value=>value.Env=="BB_AUDIO");
            var option = new Option(sound.Group,sound.Title,sound.Key,OptionKind.Toggle,true,Default:sound.Default);
            profile.WriteOption(option,sound.On);
            Check(sound.On=="none" && profile.IsOn(option) && profile.Value(option)=="none",
                "Run without sound displays its non-boolean active value correctly");
            profile.WriteOption(option,sound.Off);
            Check(!profile.IsOn(option) && profile.Value(option)==sound.Default,
                "Run without sound can be switched back to the engine default");
            Check(File.ReadAllText(ini).Contains("future_key=keep"),"reset tests leave the original fixture INI untouched until Apply");
        }
        finally
        {
            Paths.IniPath=oldIni; Paths.ConfigFile=oldConfig; Paths.DataDir=oldData;
            Paths.EngineDir=oldEngine; Paths.ConfigDir=oldConfigDir;
        }
    }
}
