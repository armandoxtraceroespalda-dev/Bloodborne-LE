using BloodborneLauncher.Native.Rendering;

namespace BloodborneLauncher.Native;
internal static class SectionIconTests
{
    public static void Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        foreach (string title in new[] { "Screen", "Game effects", "Picture", "Upscaler", "HDR", "Info", "Experimental" })
            if (SectionIcons.For(1,title) != GraphicsSectionIcons.For(title))
                throw new InvalidOperationException("existing graphics icon changed");
        Check(true,"all seven existing graphics section icons are preserved exactly");
        foreach (var item in new[] {
            (2,"Language"), (3,"Keybindings"), (4,"Gameplay Tweaks"),
            (5,"Play"), (5,"Server"), (5,"The Hunter's Dream account"), (5,"shadNet account"), (5,"Connection"),
            (6,"All achievements"), (6,"Notification"),
            (7,"Bloodborne PC"), (7,"Launch"), (7,"About"), (7,"Performance"), (7,"Files"), (7,"Diagnostics"),
            (8,"Mods"), (8,"Third-party patches"), (8,"Game output"), (8,"Graphics"), (8,"Input"), (8,"Sound"), (8,"Game fixes"),
            (9,"Items"), (9,"Character"), (9,"Blood Echoes") })
            if (SectionIcons.For(item.Item1,item.Item2).Length != 1)
                throw new InvalidOperationException("missing heading icon: " + item.Item2);
        Check(true,"sections across Game, Controls, Tweaks, Online, Achievements, Settings, More and Cheats resolve a glyph");
        Check(SectionIcons.For(0,"Language") == "", "Start receives no extra heading icons");
        Check(SectionIcons.For(7,"Files","existing-icon") == "existing-icon" &&
            SectionIcons.For(1,"Screen","custom-graphics-icon") == "custom-graphics-icon",
            "an existing explicit icon always takes precedence over the new catalog");
        Check(SectionIcons.For(8,"Future engine section") == "\uE713",
            "future engine sections receive a neutral settings symbol");
        _ = SectionIcons.For(7,"Files");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<100000;i++) _ = SectionIcons.For(7,"Files");
        Check(GC.GetAllocatedBytesForCurrentThread() == before,
            "100000 heading icon lookups allocate no managed bytes");
    }
}
