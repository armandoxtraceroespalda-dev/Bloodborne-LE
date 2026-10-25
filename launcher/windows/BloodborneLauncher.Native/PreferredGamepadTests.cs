using System.Runtime.InteropServices;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
namespace BloodborneLauncher.Native;
internal static class PreferredGamepadTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetMenu(uint buttons,short x,short y,short scroll);
    public static void Run(string parent)
    {
        static void Check(bool value,string label) { if(!value)throw new InvalidOperationException(label);Console.WriteLine("PASS "+label); }
        const string first="000102030405060708090a0b0c0d0e0f", second="101112131415161718191a1b1c1d1e1f";
        var devices=new[]{new SdlPad.Device("DualSense Test",true){Guid=first},new SdlPad.Device("Xbox Test",true){Guid=second},
            new SdlPad.Device("Duplicate",true){Guid=first},new SdlPad.Device("Unmapped",false){Guid=second},new SdlPad.Device("Zero",true){Guid=new string('0',32)}};
        var option=PreferredGamepads.Option(devices,"");
        Check(option.Kind==OptionKind.Choice && option.Key=="env:BB_GAMEPAD" && option.Choices!.Count==3 && option.Choices[0].Value=="",
            "preferred gamepad lists detected GUIDs once and keeps Automatic as the empty default");
        UiText.UseTextLanguage("3");
        Check(option.Choices![1].Label=="DualSense Test · "+first && PreferredGamepads.Label(devices,first)=="DualSense Test",
            "physical gamepad identities stay untranslated and collapsed selection shows their readable name");
        UiText.UseTextLanguage("1");
        var unavailable=PreferredGamepads.Option(Array.Empty<SdlPad.Device>(),first);
        Check(unavailable.Choices!.Count==2 && unavailable.Choices[1].Value==first,
            "disconnecting a preferred gamepad preserves the saved GUID and Automatic remains selectable");
        Check(PreferredGamepads.Option(Array.Empty<SdlPad.Device>(),"").Choices!.Count==1,
            "missing SDL or controllers still leaves a usable Automatic option");
        string oldIni=Paths.IniPath,oldConfig=Paths.ConfigFile,oldData=Paths.DataDir,oldEngine=Paths.PortDir,oldConfigDir=Paths.ConfigDir;
        try
        {
            string root=Path.Combine(parent,"preferred-gamepad-tests");Directory.CreateDirectory(root);
            string ini=Path.Combine(root,"lantern.ini"),config=Path.Combine(root,"settings.json");
            File.WriteAllText(ini,"input_mode=keyboard_mouse\nfuture_key=keep\n");File.WriteAllText(config,"{}");
            var profile=new ProfileSession(new[]{"--ini",ini,"--config",config,"--assets",root,"--profile",Path.Combine(root,"staging")});
            profile.WriteOption(option,first);
            Check(profile.SourceSettings().Str("env:BB_GAMEPAD")==first && profile.InputDevice=="keyboard_mouse",
                "choosing a preferred GUID uses the existing engine setting without switching input mode");
            ControlDefaults.Reset(profile,"keyboard_mouse",true);
            Check(profile.Str(PreferredGamepads.Key)==first,"resetting keyboard controls preserves the preferred gamepad");
            profile.Input("controller");ControlDefaults.Reset(profile,"controller",false);
            Check(profile.Str(PreferredGamepads.Key)==first,"resetting gamepad bindings alone preserves device preference");
            ControlDefaults.Reset(profile,"controller",true);
            Check(profile.Str(PreferredGamepads.Key)=="","full gamepad reset returns device preference to Automatic");
            profile.WriteOption(option,second);profile.WriteOption(option,"");
            Check(profile.Str(PreferredGamepads.Key)=="" && !EngineSwitches.Changed(profile.SourceSettings()).Any(v=>v.Name=="BB_GAMEPAD"),
                "Automatic removes BB_GAMEPAD rather than forcing a device or disabling detection");
            string? fixture=Environment.GetEnvironmentVariable("BB_NATIVE_TEST_SDL_GUID");
            if(!OperatingSystem.IsWindows() && !string.IsNullOrEmpty(fixture))
            {
                SdlPad.Stop();Paths.EngineDir=root;Directory.CreateDirectory(Paths.BinariesDir);
                File.Copy(fixture,Path.Combine(Paths.BinariesDir,"SDL3.dll"),true);
                var discovered=SdlPad.Devices();
                Check(discovered is {Count:3} && discovered[0].Guid==first && discovered[1].Guid==second && !discovered[2].Supported,
                    "real SDL delegates marshal GUID structs and ASCII buffers correctly through the native fixture");
                SdlPad.Stop();
                Check(SdlPad.Devices()?[0].Guid==first,"SDL GUID delegates reload safely after module release");
                nint native=NativeLibrary.Load(Path.Combine(Paths.BinariesDir,"SDL3.dll"));
                try
                {
                    if(NativeLibrary.TryGetExport(native,"fixture_set_menu",out var export))
                    {
                        var set=Marshal.GetDelegateForFunctionPointer<SetMenu>(export);
                        set(3,18000,-22000,24000);
                        var menu=SdlPad.ReadMenuInput();
                        Check(menu.Connected==2 && menu.Buttons==3 && menu.LeftX==18000 && menu.LeftY==-22000 && menu.RightY==24000,
                            "SDL launcher navigation marshals real native button bits and signed axes without a Held set");
                        set(0,0,0,0);
                    }
                }
                finally { NativeLibrary.Free(native); }
            }
        }
        finally
        {
            SdlPad.Stop();Paths.IniPath=oldIni;Paths.ConfigFile=oldConfig;Paths.DataDir=oldData;Paths.EngineDir=oldEngine;Paths.ConfigDir=oldConfigDir;UiText.UseTextLanguage("1");
        }
    }
}
