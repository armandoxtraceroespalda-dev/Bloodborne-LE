using System.Text.Json.Nodes;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class EnginePresetTests
{
    public static void Run(string parent)
    {
        static void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }
        string oldIni=Paths.IniPath, oldConfig=Paths.ConfigFile, oldData=Paths.DataDir, oldEngine=Paths.PortDir, oldConfigDir=Paths.ConfigDir;
        try
        {
            string root=Path.Combine(parent,"engine-preset-tests");Directory.CreateDirectory(root);
            string ini=Path.Combine(root,"lantern.ini"), config=Path.Combine(root,"settings.json");
            File.WriteAllText(ini,"future_key=keep\noutput_res=2560x1440\n");
            File.WriteAllText(config,"{\"exp_fast_draws\":true,\"exp_compile_skip\":true,\"exp_vram_budget\":true,\"legacy_shaders\":true,\"exp_occlusion\":true,\"future_setting\":\"keep\",\"text_language\":\"1\",\"legacy_shaders_default_98\":true,\"occlusion_off_99\":true,\"perf_defaults_b4\":true}");
            var args=new[]{"--ini",ini,"--config",config,"--assets",root,"--profile",Path.Combine(root,"staging")};
            var profile=new ProfileSession(args);
            var row=new Option("Engine preset","Preset","native-engine-preset",OptionKind.Choice,true,EnginePreset.Choices,Default:EnginePreset.New);
            Check(profile.Value(row)==EnginePreset.New,"engine preset detects the existing New state without changing saved settings");
            var before=profile.SourceSettings().All.ToDictionary(p=>p.Key,p=>p.Value?.ToJsonString());
            profile.WriteOption(row,EnginePreset.Legacy);
            var legacy=profile.SourceSettings();
            var after=legacy.All.ToDictionary(p=>p.Key,p=>p.Value?.ToJsonString());
            var changed=before.Keys.Union(after.Keys).Where(k=>before.GetValueOrDefault(k)!=after.GetValueOrDefault(k)).Order().ToArray();
            Check(profile.Value(row)==EnginePreset.Legacy && changed.SequenceEqual(new[]{"exp_compile_skip","exp_fast_draws","exp_vram_budget"}),
                "Legacy changes exactly the same three feature keys as upstream");
            Check(legacy.Bool("legacy_shaders") && legacy.Bool("exp_occlusion") && legacy.Str("future_setting")=="keep" && profile.Ini.Get("output_res")=="2560x1440",
                "engine presets preserve independent shader/compiler, occlusion, graphics and unknown settings");
            bool environmentAvailable = OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BB_NATIVE_TEST_USER32"));
            string[] switches={"BB_SUBMIT_END_TASKS","BB_MEMO_REVALIDATE","BB_SAMPLER_MEMO","BB_DRAIN_SLEEP","BB_FAULT_SKIP_TEXTURES","BB_SKIP_UNCOMPILED","BB_LATE_PIPELINE_BIND","BB_PIPELINE_TAKE","BB_GC_PRESSURE_PERCENT","BB_GC_PRESSURE_SECONDS"};
            if (environmentAvailable)
            {
                var legacyEnvironment=GameEnvironment.Build(legacy);
                Check(switches.All(k=>legacyEnvironment.GetValueOrDefault(k)=="0"),"Legacy reaches the real game environment for draw, shader and VRAM behavior");
            }
            profile.Set("exp_fast_draws","true",true);
            Check(profile.Value(row)==EnginePreset.Custom,"changing a single feature updates the preset to Custom automatically");
            string custom=profile.Settings.ToJsonString();
            profile.WriteOption(row,EnginePreset.Custom);
            Check(profile.Settings.ToJsonString()==custom && profile.Value(row)==EnginePreset.Custom,
                "selecting Custom leaves individual values intact, matching upstream");
            profile.WriteOption(row,EnginePreset.New);
            var current=profile.SourceSettings();
            if (environmentAvailable)
            {
                var newEnvironment=GameEnvironment.Build(current);
                Check(profile.Value(row)==EnginePreset.New && switches.All(k=>!newEnvironment.ContainsKey(k)),
                    "New reenables the three features and removes their Legacy environment overrides");
                current.Set("extra_env","BB_SUBMIT_END_TASKS=7");
                Check(GameEnvironment.Build(current).GetValueOrDefault("BB_SUBMIT_END_TASKS")=="7",
                    "explicit extra environment variables retain upstream override precedence");
            }
            else Console.WriteLine("SKIP preset environment checks: Windows or the monitor fixture is required.");
            profile.SavePreview();
            var reopened=new ProfileSession(args);
            Check(reopened.Value(row)==EnginePreset.New && File.ReadAllText(config).Contains("future_setting"),
                "engine preset survives staged profile reload without overwriting the original config");
        }
        finally
        {
            Paths.IniPath=oldIni;Paths.ConfigFile=oldConfig;Paths.DataDir=oldData;
            Paths.EngineDir=oldEngine;Paths.ConfigDir=oldConfigDir;
            UiText.UseTextLanguage("1");
        }
    }
}
