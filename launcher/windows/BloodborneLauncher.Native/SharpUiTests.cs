using System.Text.Json.Nodes;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class SharpUiTests
{
    public static void Run(string parent)
    {
        static void Check(bool value,string label) { if(!value)throw new InvalidOperationException(label);Console.WriteLine("PASS "+label); }
        var rows=GraphicsSchema.Build().ToArray();
        var option=rows.Single(o=>o.Key=="exp_sharp_ui");
        Check(option.Section=="Screen" && option.Title=="Sharp UI" && option.Kind==OptionKind.Toggle && option.Setting && option.Default=="false",
            "Sharp UI is a Screen toggle backed by the original launcher setting, not an unrelated INI key");
        Check(Array.FindIndex(rows,o=>o.Key=="exp_sharp_ui")==Array.FindIndex(rows,o=>o.Key=="output_res")+1,
            "Sharp UI appears immediately after Output resolution");
        string oldIni=Paths.IniPath,oldConfig=Paths.ConfigFile,oldData=Paths.DataDir,oldEngine=Paths.PortDir,oldConfigDir=Paths.ConfigDir;
        try
        {
            string root=Path.Combine(parent,"sharp-ui-tests");Directory.CreateDirectory(root);
            string ini=Path.Combine(root,"lantern.ini"),config=Path.Combine(root,"settings.json");
            File.WriteAllText(ini,"output_res=2560x1440\nfuture_key=keep\n");
            File.WriteAllText(config,"{\"exp_sharp_ui\":false,\"future_setting\":\"keep\",\"text_language\":\"1\"}");
            var args=new[]{"--ini",ini,"--config",config,"--assets",root,"--profile",Path.Combine(root,"staging")};
            var profile=new ProfileSession(args);
            Check(!profile.IsOn(option),"Sharp UI reads the existing disabled preference without enabling it on page open");
            profile.WriteOption(option,"true");
            Check(profile.IsOn(option) && profile.SourceSettings().Bool("exp_sharp_ui"),"enabling Sharp UI reaches the shared LauncherSettings model");
            bool environmentAvailable=OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BB_NATIVE_TEST_USER32"));
            if(environmentAvailable)
                Check(GameEnvironment.Build(profile.SourceSettings()).GetValueOrDefault("BB_SHARP_UI")=="1",
                    "enabled Sharp UI emits the BB_SHARP_UI=1 startup flag consumed by engine/start/patches.cpp");
            profile.WriteOption(option,"false");
            if(environmentAvailable)
                Check(!GameEnvironment.Build(profile.SourceSettings()).ContainsKey("BB_SHARP_UI"),
                    "disabling Sharp UI removes its override so the engine uses its original UI viewport");
            Check(profile.Ini.Get("output_res")=="2560x1440" && profile.Ini.Raw("exp_sharp_ui")==null && profile.Str("future_setting")=="keep",
                "Sharp UI changes neither output resolution nor unrelated settings and creates no unsupported INI key");
            profile.WriteOption(option,"true");profile.SavePreview();
            var reopened=new ProfileSession(args);
            Check(reopened.IsOn(option),"Sharp UI survives a staged launcher restart");
            reopened.EditSource((settings,input)=>EnginePreset.Apply(settings,EnginePreset.Legacy));
            Check(reopened.IsOn(option),"Sharp UI remains independent of New/Legacy engine presets");
            reopened.ApplyToGame();
            var saved=JsonNode.Parse(File.ReadAllText(config));
            Check(saved?["exp_sharp_ui"]?.GetValue<bool>()==true && saved?["future_setting"]?.ToString()=="keep" && File.ReadAllText(ini).Contains("output_res=2560x1440"),
                "applying launcher settings persists Sharp UI as a boolean while preserving user files");
        }
        finally
        {
            Paths.IniPath=oldIni;Paths.ConfigFile=oldConfig;Paths.DataDir=oldData;Paths.EngineDir=oldEngine;Paths.ConfigDir=oldConfigDir;UiText.UseTextLanguage("1");
        }
    }
}
