using BloodborneLauncher.Native.Interaction;
using BloodborneLauncher.Native.Rendering;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Core;
using Vortice.Mathematics;

namespace BloodborneLauncher.Native;
internal static class ScrollTests
{
    static void Check(bool yes, string name)
    {
        if (!yes)
            throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }

    public static void Run(ProfileSession profile)
    {
        var b = new Rect(10, 1000, 120, 30);
        var viewport = new Rect(0, 100, 500, 300);
        var track = new Rect(490, 100, 8, 300);
        var op = new Op(Ink.Text, b, new Color4(1, 1, 1, 1), "Retained row", Scrollable: true);
        var key = TextKey.From(op);
        for (int i = 0; i < 1000; i++)
        {
            _ = ScrollGeometry.Screen(b, true, i);
            _ = ScrollGeometry.Thumb(track, 1000, i);
            _ = TextKey.From(op) == key;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        for (int i = 0; i < 100000; i++)
        {
            float offset = i % 1000;
            sum += ScrollGeometry.Screen(b, true, offset).Y + ScrollGeometry.Thumb(track, 1000, offset).Height;
            if (TextKey.From(op) != key)
                throw new InvalidOperationException("Scroll changed a text key.");
        }

        Check(GC.GetAllocatedBytesForCurrentThread() == before && sum > 0, "100000 scroll projections allocate no managed bytes and retain text identity");
        Check(ScrollGeometry.Hit(b, true, viewport, 20, 120, 900), "scroll rendering and hit testing share the same projection");
        Check(!ScrollGeometry.Hit(b, true, viewport, 20, 60, 950), "clipped rows cannot receive clicks");
        Check(ScrollGeometry.Screen(b, false, 900) == b, "sidebar and caption stay fixed");
        Check(Math.Abs(ScrollGeometry.Thumb(track, 1000, 1000).Bottom - track.Bottom) < .001, "scrollbar reaches the bottom");
        foreach (int page in Enumerable.Range(2, 8))
            Check(PageCatalog.Build(page, profile).Count > 0, "native form: " + PageCatalog.Titles[page]);
        profile.Set("pad.cross", "b");
        profile.UseProfile(ControlProfiles.BuiltIns.First(p => p.Name == ControlProfiles.DarkSouls3Name));
        Check(profile.Ini.Get("pad.cross") == "b" && profile.Ini.Get("bind.switch_right") == "Left Shift + WheelUp" && profile.Ini.Get("bind.switch_left") == "Left Shift + WheelDown", "source DS3 profile preserves gamepad and wheel chords");
        var control = InputMap.Controls.First(c => c.Name == "attack");
        profile.Bind(control, "Left Shift + Mouse1");
        Check(Bindings.Get(profile.Ini, control).Any(BindingExpressions.IsChord), "binding capture uses upstream chord syntax");
        profile.Set("bind.attack", null);
        profile.SavePreview();
        Check(profile.Ini.Raw("bind.attack") == null, "defaults remove binding overrides");
        profile.Set("env:BB_TEST_NATIVE", "x", true);
        profile.EditSource((s, ini) => s.Remove("env:BB_TEST_NATIVE"));
        Check(profile.Settings["env:BB_TEST_NATIVE"] == null, "source setting removal is staged");
        var engine = EngineSwitches.All.First(i => i.Kind == SwitchKind.Toggle);
        profile.WriteOption(new Option(engine.Group, engine.Title, engine.Key, OptionKind.Toggle, true, Default: engine.Default), engine.On);
        Check(EngineSwitches.Get(profile.SourceSettings(), engine) == engine.On, "source engine switch validation is preserved");
        Check(KeyboardNames.FromVirtualKey(0x10, 0x360000) == "Right Shift" && KeyboardNames.FromVirtualKey(0x11, 0x1000000) == "Right Ctrl", "capture distinguishes left/right modifiers");
        profile.WithSidecars(() =>
        {
            ProfileSession.AtomicJson(Path.Combine(Paths.DataDir, "mods.json"), new System.Text.Json.Nodes.JsonObject { ["native-test"] = true });
            return true;
        }, "mods.json");
        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(profile.OriginalIni)!, "mods.json")), "mod selections remain staged until Apply");
    }
}
