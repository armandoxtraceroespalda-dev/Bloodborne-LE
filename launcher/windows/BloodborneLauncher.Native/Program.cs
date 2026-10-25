using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;

namespace BloodborneLauncher.Native;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--prepare-back-sound")
        {
            try
            {
                return NativeBackSound.Ensure(args[1], args[2], CancellationToken.None) ? 0 : 2;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 2;
            }
        }

        if (args.Contains("--self-test"))
            return SelfTest.Run();
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The native UI runs on Windows. Use --self-test for platform-independent contracts.");
            return 1;
        }

        try
        {
            Win32.SetProcessDpiAwarenessContext(new nint(-4));
            Win32.CoInitializeEx(0, 2);
            try
            {
                var profile = new ProfileSession(args);
                string[] required =
                {
                    "background.png",
                    "menu-glow-half.png",
                    "menu-underline.png",
                    "menu-book-leaf.png",
                    "menu-book-panel.png",
                    "BloodborneMenu.ttf"
                };
                var missing = required.Where(n => !File.Exists(Path.Combine(profile.Assets, n))).ToArray();
                if (missing.Length > 0)
                    throw new FileNotFoundException("Keep your extracted gui folder beside the launcher, or pass --assets <gui folder>.\n\n" + profile.Assets + "\nMissing: " + string.Join(", ", missing));
                using var window = new NativeWindow(profile, args);
                return window.Run();
            }
            finally
            {
                Win32.CoUninitialize();
            }
        }
        catch (Exception e)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "native-preview-error.txt");
            File.WriteAllText(path, e.ToString());
            Win32.MessageBox(0, e.GetBaseException().Message + "\n\n" + path, "Bloodborne Launcher", 0x10);
            return 1;
        }
    }
}
