using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using System.Diagnostics;

namespace BloodborneLauncher.Native;
internal static class RuntimeUserPathsTests
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BB_NATIVE_TEST_USER32")))
        {
            Console.WriteLine("SKIP runtime environment fixture: set BB_NATIVE_TEST_USER32 to the compiled monitor stub.");
            return;
        }
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        string root = Path.Combine(Path.GetTempPath(), "runtime-paths-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "current", "engine"), user = Path.Combine(data, "user");
        string retired = Path.Combine(root, "BetterKBM-engine-update36", "user");
        string? inherited = Environment.GetEnvironmentVariable("BB_GPU_USER_DIR");
        var previous = (Paths.IniPath, Paths.ConfigDir, Paths.ConfigFile, Paths.DataDir, Paths.PortDir, Workspace.Current.Ini);
        Directory.CreateDirectory(data);
        try
        {
            string config = Path.Combine(root, "settings.json"), ini = Path.Combine(data, "lantern.ini");
            File.WriteAllText(config, "{\"text_language\":\"1\"}");
            File.WriteAllText(ini, "output_res=2560x1440\nkey.cross=E\n");
            var profile = new ProfileSession(new[] { "--config", config, "--ini", ini, "--profile", Path.Combine(root, "staged"), "--assets", root });
            Environment.SetEnvironmentVariable("BB_GPU_USER_DIR", retired);
            var settings = profile.SourceSettings();
            var environment = GameEnvironment.Build(settings);
            Check(environment.TryGetValue("BB_GPU_USER_DIR", out var gpu) && gpu == user, "the real launch environment replaces an inherited retired GPU root");
            Check(environment["BB_USER_DIR"] == user, "saves and GPU root use current paths before starting the engine");
            var child = new ProcessStartInfo("fixture");
            foreach (var pair in environment) child.Environment[pair.Key] = pair.Value;
            // bb-start creates the directory named by this value before launching
            // bb-probe; exercising that boundary must not recreate the old root.
            Directory.CreateDirectory(child.Environment["BB_GPU_USER_DIR"]!);
            Check(!Directory.Exists(retired) && Directory.Exists(user), "engine startup directory creation cannot recreate the retired user folder");
            Check(Environment.GetEnvironmentVariable("BB_GPU_USER_DIR") == retired, "the fix does not mutate the parent environment or Windows settings");
            settings.Set("extra_env", "BB_GPU_USER_DIR=" + retired + " BB_USER_DIR=" + Path.Combine(retired, "profiles", "Player"));
            environment = GameEnvironment.Build(settings);
            Check(environment["BB_GPU_USER_DIR"] == user && environment["BB_USER_DIR"] == user, "retired paths in Extra variables cannot undo migration");
            settings.Set("player_profiles", "Player"); settings.Set("player_profile", "Player");
            environment = GameEnvironment.Build(settings);
            Check(environment["BB_GPU_USER_DIR"] == user && environment["BB_USER_DIR"] == Path.Combine(user, "profiles", "Player"), "GPU caches stay shared while saves use the active player profile");
            string custom = Path.Combine(root, "custom-cache");
            settings.Set("extra_env", ""); Environment.SetEnvironmentVariable("BB_GPU_USER_DIR", custom);
            Check(GameEnvironment.Build(settings)["BB_GPU_USER_DIR"] == custom, "a custom inherited GPU cache root remains supported");
            settings.Set("extra_env", "BB_GPU_USER_DIR=" + Path.Combine(root, "explicit-cache"));
            Check(GameEnvironment.Build(settings)["BB_GPU_USER_DIR"] == Path.Combine(root, "explicit-cache"), "an explicit custom cache root takes precedence over inheritance");
            settings.Set("extra_env", ""); Environment.SetEnvironmentVariable("BB_GPU_USER_DIR", null);
            Check(GameEnvironment.Build(settings)["BB_GPU_USER_DIR"] == user, "the default GPU root is explicit even without inherited variables");
            settings.Set("safe_mode_next", "true"); Environment.SetEnvironmentVariable("BB_GPU_USER_DIR", retired);
            Check(GameEnvironment.Build(settings)["BB_GPU_USER_DIR"] == user, "safe mode also avoids the retired GPU root");
            var portable = new Dictionary<string, string> { ["BB_GPU_USER_DIR"] = @"C:\Users\Admin\Documents\GitHub\BetterKBM-engine-update36\user\" };
            RuntimeUserPaths.Resolve(portable, data, user, null);
            Check(portable["BB_GPU_USER_DIR"] == user, "Windows separators and trailing slashes are recognized");
            foreach (string name in new[] { "BetterKBM-engine", "BetterKBM-engine-expanded", "betterkbm-engine-update99" })
            {
                var test = new Dictionary<string, string>();
                RuntimeUserPaths.Resolve(test, data, user, Path.Combine(root, name, "user"));
                Check(test["BB_GPU_USER_DIR"] == user, "former generated GPU root is redirected: " + name);
            }
            Check(profile.Ini.Get("key.cross") == "E" && profile.Ini.Get("output_res") == "2560x1440", "runtime directory resolution leaves game settings unchanged");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BB_GPU_USER_DIR", inherited);
            Paths.IniPath = previous.Item1; Paths.ConfigDir = previous.Item2; Paths.ConfigFile = previous.Item3;
            Paths.DataDir = previous.Item4; Paths.EngineDir = previous.Item5; Workspace.Current.Ini = previous.Item6;
            Directory.Delete(root, true);
        }
    }
}
