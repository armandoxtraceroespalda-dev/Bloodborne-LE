using System.Text.Json.Nodes;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Interaction;
using BloodborneLauncher.Native.Platform;

namespace BloodborneLauncher.Native;
internal static class LauncherSearchPerformanceTests
{
    public static void Run(string parent)
    {
        static void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);Console.WriteLine("PASS "+label);}
        string oldIni=Paths.IniPath,oldConfig=Paths.ConfigFile,oldData=Paths.DataDir,oldEngine=Paths.PortDir,oldDir=Paths.ConfigDir;
        try
        {
            string root=Path.Combine(parent,"search-perf");Directory.CreateDirectory(root);
            string ini=Path.Combine(root,"lantern.ini"),config=Path.Combine(root,"settings.json");
            File.WriteAllText(ini,"input_mode=keyboard_mouse\noutput_res=2560x1440\nfuture_key=keep\n");
            File.WriteAllText(config,"{\"text_language\":\"1\",\"future_object\":{\"n\":7},\"future_null\":null,\"legacy_shaders_default_98\":true,\"occlusion_off_99\":true,\"perf_defaults_b4\":true}");
            var profile=new ProfileSession(new[]{"--ini",ini,"--config",config,"--assets",root,"--profile",Path.Combine(root,"staging")});
            var snapshot=profile.ReadSettings();
            for(int i=0;i<1000;i++)_=profile.ReadSettings();
            long before=GC.GetAllocatedBytesForCurrentThread();bool same=true;
            for(int i=0;i<10000;i++)same&=ReferenceEquals(snapshot,profile.ReadSettings());
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(same && allocated==0 && profile.SettingsSnapshotLoads==1,
                "10000 rendering settings reads allocate zero managed bytes and share one normalized snapshot");
            var clone=profile.SourceSettings();clone.Set("text_language","3");
            Check(profile.ReadSettings().Str("text_language")=="1" && clone.All.Any(p=>p.Key=="future_null" && p.Value==null),
                "editing clones cannot mutate rendering state and preserve null/unknown JSON fields");
            profile.Set("text_language","3",true);
            var changed=profile.ReadSettings();long loads=profile.SettingsSnapshotLoads;
            profile.Set("text_language","3",true);
            Check(changed.Str("text_language")=="3" && ReferenceEquals(changed,profile.ReadSettings()) && profile.SettingsSnapshotLoads==loads,
                "actual settings changes invalidate once; writing an unchanged value does not invalidate or reload");
            profile.EditSource((settings,input)=>settings.Set("exp_sharp_ui",true));
            Check(profile.ReadSettings().Bool("exp_sharp_ui"),"source-linked edits invalidate the cached rendering snapshot");
            string originalIni=profile.Ini.Get("input_mode"),original=profile.Settings.ToJsonString();
            bool localized=true,engineLocalized=true;
            string[] languages={"1","3","20","2","4","5","17","8","0"};
            foreach(string language in languages)
            {
                UiText.UseTextLanguage(language);
                var index=SettingsSearchIndex.Build(profile,false);
                string title=UiText.T("Sharp UI");
                localized &= index.Find(title).Any(r=>r.Key=="exp_sharp_ui" && r.Page==1);
                string engine=UiText.T("Run without sound");
                engineLocalized &= EngineSwitches.All.Single(e=>e.Env=="BB_AUDIO").Title==engine && index.Find(engine).Any(r=>r.Key=="env:BB_AUDIO");
            }
            Check(localized,"global search finds the currently translated Sharp UI name in every supported launcher language");
            Check(engineLocalized,"engine switch names and search results follow language changes without stale initialized titles");
            UiText.UseTextLanguage("3");
            var spanish=SettingsSearchIndex.Build(profile,false);
            Check(spanish.Find("INTERFAZ NITIDA").Any(r=>r.Key=="exp_sharp_ui"),"search ignores case and accents in translated Spanish setting names");
            UiText.UseTextLanguage("1");var english=SettingsSearchIndex.Build(profile,false);
            Check(english.Find("deadzone").Any(r=>r.Page==3 && r.Device=="controller") && english.Find("Turning speed").Any(r=>r.Device=="keyboard_mouse"),
                "search indexes both input modes without switching the game's selected device");
            Check(profile.Ini.Get("input_mode")==originalIni && profile.Settings.ToJsonString()==original,
                "building and querying the global search index does not change user configuration");
            Check(!english.Find("stamina").Any(r=>r.Page==9) && SettingsSearchIndex.Build(profile,true).Find("stamina").Any(r=>r.Page==9),
                "hidden cheats stay out of search until the user unlocks the Cheats tab");
            Check(english.Find(" ").Count==0 && english.Find("a",3).Count<=3 && SettingsSearchIndex.Normalize("\ud800").Length==1,
                "search handles empty/partial Unicode queries and bounds result count");
            var slider=GraphicsSchema.Build().First(r=>r.Kind==OptionKind.Slider);
            Check(slider.Key.Length>0 && profile.SettingsSnapshotLoads>0,"search uses metadata rather than constructing page views");
            var queue=new Queue<Action>();long now=1000;int collections=0;
            using var reclaim=new NavigationMemoryReclaimer(queue.Enqueue,()=>collections++,()=>true,()=>now,750);
            reclaim.Request();reclaim.TryStart(true);queue.Dequeue()();
            reclaim.Request();Check(!reclaim.TryStart(true),"rapid cleanup requests remain pending during the navigation cooldown");
            now=1750;Check(reclaim.TryStart(true),"the latest pending cleanup runs after the cooldown without another request");queue.Dequeue()();
            Check(collections==2,"cleanup throttling coalesces work without discarding the final requested collection");
            Console.WriteLine($"PERF settings_reads=10000 managed_allocated_bytes={allocated} snapshot_loads_initial=1 search_entries={english.Count}");
        }
        finally{Paths.IniPath=oldIni;Paths.ConfigFile=oldConfig;Paths.DataDir=oldData;Paths.EngineDir=oldEngine;Paths.ConfigDir=oldDir;UiText.UseTextLanguage("1");}
        TestPad(Check);
    }
    static void TestPad(Action<bool,string> check)
    {
        var navigation=new MenuGamepadState();
        check(navigation.Update(default,0)==default,"disconnected controllers produce no launcher actions");
        var neutral=new SdlPad.MenuInput(0,0,0,0,1);
        navigation.Update(neutral with{Buttons=1},0);
        check(navigation.Update(neutral with{Buttons=1},.1).Pressed==0,"a button held while connecting cannot accidentally launch the game");
        navigation.Update(neutral,.2);
        check(navigation.Update(neutral with{Buttons=1},.3).Pressed==1 && navigation.Update(neutral with{Buttons=1},1).Pressed==0,
            "confirm uses rising edges and never repeats while held");
        navigation.Reset();navigation.Update(neutral,0);
        check(navigation.Update(neutral with{LeftY=-24000},.1).Direction==1 && navigation.Update(neutral with{LeftY=-24000},.2).Direction==0 &&
            navigation.Update(neutral with{LeftY=-24000},.46).Direction==1,"stick navigation has an initial delay and controlled repeat");
        check(navigation.Update(neutral with{LeftY=24000},.5).Direction==2,"opposite navigation changes direction immediately");
        navigation.Reset();navigation.Update(neutral,0);
        check(navigation.Update(neutral with{LeftX=1000,LeftY=-1000},.1).Direction==0 && navigation.Update(neutral with{RightY=32767},.2).Scroll>.99f,
            "navigation deadzones suppress drift while the right stick provides proportional scrolling");
        for(int i=0;i<1000;i++)_=navigation.Update(neutral,2+i*.033);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)_=navigation.Update(neutral,100+i*.033);
        check(GC.GetAllocatedBytesForCurrentThread()-before==0,"10000 gamepad navigation state updates allocate no managed memory");
    }
}
