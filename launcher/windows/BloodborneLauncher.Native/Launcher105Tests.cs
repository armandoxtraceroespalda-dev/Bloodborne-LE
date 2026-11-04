using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;

namespace BloodborneLauncher.Native;

internal static class Launcher105Tests
{
    internal static void Run(ProfileSession profile)
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }
        Check(ControlDeviceIcons.Mouse != ControlDeviceIcons.Gamepad,
            "input selector has distinct mouse and gamepad symbols");
        Check(ControlDeviceIcons.Bindings("keyboard_mouse") == ControlDeviceIcons.Keyboard &&
            ControlDeviceIcons.Bindings("controller") == ControlDeviceIcons.Gamepad,
            "Keybindings heading follows the selected input device");
        var turning = PageCatalog.Build(3, profile, "keyboard_mouse").Single(o => o.Key == "stick_turn_rate");
        Check(turning.Default == "800", "keyboard movement default is 800 degrees per second");
        Check(!PageCatalog.Build(3, profile, "controller").Any(o => o.Key == "stick_turn_rate"),
            "keyboard turning default does not become a gamepad setting");
        var menus = GraphicsSchema.Build().Where(o => o.Section == "Menus").ToArray();
        Check(menus.Select(o => o.Key).ToHashSet().SetEquals(new[] { "ui_4k", "action:ui4k-build", "action:ui4k-remove" }),
            "Graphics includes the 4K switch, preparation and removal actions");
        Check(GraphicsSectionIcons.For("Menus").Length == 1, "menu textures section has a native heading symbol");
        var online = PageCatalog.Build(5, profile);
        var port = online.Single(o => o.Key == "online_p2p_port");
        Check(port.Default == "" && port.Hint?.Contains("3658") == true,
            "default P2P port is a hint, not a saved configuration value");
        Check(online.Single(o => o.Key == "online_server").Choices!.Select(c => c.Value)
            .ToHashSet().SetEquals(new[] { "huntersdream", SuperMedo.Id }),
            "visible server selector contains both supported providers");
        foreach (string key in new[] { "seamless_enabled", "seamless_role", "action:room-copy", "action:room-paste", "voice_enabled", "voice_volume" })
            Check(online.Any(o => o.Key == key), "online interface includes " + key);
        _ = ControlDeviceIcons.Bindings("controller");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) _ = ControlDeviceIcons.Bindings("controller");
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "device icon changes introduce no per-lookup allocations");
    }
}
