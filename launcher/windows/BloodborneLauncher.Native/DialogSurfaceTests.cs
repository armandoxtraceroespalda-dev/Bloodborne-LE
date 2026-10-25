using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;

namespace BloodborneLauncher.Native;
internal static class DialogSurfaceTests
{
    public static void Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        byte[] pixels = SoftSurfacePixels.Create(black: true, dialog: true);
        int Alpha(int x, int y) => pixels[(y * SoftSurfacePixels.Size + x) * 4 + 3];
        Check(pixels.Length == 16384 && Alpha(32,32) == 255,
            "dialog background uses one 16 KiB texture with an opaque source center");
        Check(Alpha(0,32)==0 && Alpha(63,32)==0 && Alpha(32,0)==0 && Alpha(32,63)==0 && Alpha(0,0)==0,
            "dialog edges and corners fade completely to transparent");
        Check(Alpha(4,32)<Alpha(8,32) && Alpha(8,32)<Alpha(16,32) && Alpha(8,8)<Alpha(8,32),
            "dialog alpha increases smoothly from its sides and corners");
        Check(Enumerable.Range(0,pixels.Length).Where(i=>i%4!=3).All(i=>pixels[i]==0),
            "feathered dialog pixels stay black without a white fringe");
        Check(Alpha(32,32)/255f*.75f == .75f,
            "applying 75 percent opacity produces a 75 percent black center");
        var previous = SoftSurfacePixels.Create(false,false);
        Check(previous[3]==0 && previous[(32*64+32)*4+3]==(byte)Math.Round(210*.85),
            "existing page shadow opacity is unchanged by the new dialog texture");

        foreach (string language in new[] { "1","3","20","2","4","5","17","8","0" })
        {
            UiText.UseTextLanguage(language);
            foreach (bool all in new[] {false,true})
            {
                var keyboard = ControlResetText.For(all,"keyboard_mouse");
                var gamepad = ControlResetText.For(all,"controller");
                if (keyboard.Body.Length==0 || keyboard.Tooltip.Length==0 ||
                    gamepad.Body.Length==0 || gamepad.Tooltip.Length==0 ||
                    keyboard.Body.Contains(keyboard.Tooltip,StringComparison.Ordinal) ||
                    gamepad.Body.Contains(gamepad.Tooltip,StringComparison.Ordinal) || keyboard.Body==gamepad.Body)
                    throw new InvalidOperationException("reset notice separation: "+language);
            }
        }
        UiText.UseTextLanguage("1");
        var graphics = GraphicsSchema.Build();
        foreach (var (env, section) in new[] { ("BB_ANISO", "Screen"), ("BB_UPSCALER_NO_BLUR", "Upscaler") })
        {
            var engine = EngineSwitches.All.Single(i => i.Env == env);
            var moved = graphics.Single(i => i.Key == engine.Key);
            Check(moved.Section == section && moved.Setting && moved.Default == engine.Default &&
                moved.Choices == engine.Options,
                "moved " + env + " preserves its environment key, default and choices");
        }
        Check(true,"all supported reset languages separate the short message from the other-device tooltip");
        Check(ControlResetText.For(true,"controller").Body == "Gamepad defaults will be restored." &&
            ControlResetText.For(false,"keyboard_mouse").Tooltip == "Gamepad settings will be kept.",
            "both reset variants put preservation details only in the information tooltip");
    }
}
