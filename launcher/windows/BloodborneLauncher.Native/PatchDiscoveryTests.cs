using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native;
internal static class PatchDiscoveryTests
{
    internal static void Run()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        string temporary = Path.Combine(Path.GetTempPath(), "patch-discovery-" + Guid.NewGuid().ToString("N"));
        string engine = Path.Combine(temporary, "package", "engine");
        string retired = Path.Combine(temporary, "BetterKBM-engine-update36");
        string builtIn = Path.Combine(engine, "patches", "Bloodborne.xml");
        var previous = (Paths.PortDir, Paths.DataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(builtIn)!);
        try
        {
            Paths.EngineDir = engine;
            Paths.DataDir = Path.Combine(temporary, "data");
            File.WriteAllText(builtIn, """
                <Patch><ID>CUSA03173</ID>
                  <Metadata Name="Resolution Patch 1280x720 (16:9)" AppVer="01.09" AppElf="eboot.bin"><Line Address="1000" Type="bytes" Value="AA" /></Metadata>
                  <Metadata Name="Available built-in fixture" AppVer="01.09" AppElf="eboot.bin"><Line Address="2000" Type="bytes" Value="AA" /></Metadata>
                </Patch>
                """);
            var settings = new LauncherSettings();
            settings.Set("patches_dir", retired);
            // The same call made by PreflightGame before bb-start is launched.
            Patches.ResolutionSetting(GameEnvironment.PatchesDir(settings));
            Check(!Directory.Exists(retired), "Play resolution preflight does not recreate a missing former engine folder");
            Patches.RegionChanging(GameEnvironment.PatchesDir(settings));
            Check(!Directory.Exists(retired), "online region preflight does not recreate a missing patch folder");
            var found = Patches.Discover(retired);
            Check(found.Count == 1 && found[0].Name == "Available built-in fixture", "built-in optional patches remain available when the custom folder is absent");
            Check(!Directory.Exists(retired), "listing patches in More is read-only for an absent custom folder");
            Patches.Discover(Path.Combine(retired, "nested", "patches"));
            Check(!Directory.Exists(retired), "discovery does not create missing parent directories either");
            Check(Mods.Discover(retired).Count == 0 && !Directory.Exists(retired), "mod discovery also leaves the retired path absent");
            Check(settings.Str("patches_dir") == retired, "discovery does not overwrite a custom folder preference");
            Check(!Directory.Exists(Paths.DataDir), "reading a fresh patch profile does not create the data directory");
            string custom = Path.Combine(temporary, "custom-patches");
            Directory.CreateDirectory(custom);
            string file = Path.Combine(custom, "Example.xml");
            const string xml = """
                <Patch><ID>CUSA03173</ID>
                  <Metadata Name="User fixture" Author="Player" AppVer="01.09" AppElf="eboot.bin" isEnabled="true"><Line Address="1000" Type="bytes" Value="BB" /><Line Address="23b9190" Type="bytes" Value="01" /></Metadata>
                </Patch>
                """;
            File.WriteAllText(file, xml);
            found = Patches.Discover(custom);
            Check(found.Count == 2 && found.Any(p => p.Name == "User fixture" && p.Author == "Player" && p.DefaultOn), "existing third-party XML patches keep their metadata and default enabled state");
            Check(Patches.ResolutionSetting(custom).Single().Name == "User fixture", "existing enabled resolution patches are still detected");
            Check(Patches.RegionChanging(custom).Single().Name == "User fixture", "existing enabled region patches are still detected");
            Check(File.ReadAllText(file) == xml, "patch files are unchanged by listing and preflight checks");
            File.Delete(file);
            Directory.Delete(custom);
            Patches.Discover(custom);
            Check(!Directory.Exists(custom), "a removed custom patch folder stays removed on refresh");
            File.Delete(builtIn);
            Directory.Delete(Path.GetDirectoryName(builtIn)!);
            Check(Patches.Discover(retired).Count == 0 && !Directory.Exists(retired), "missing built-in and custom folders are handled without creating either one");
        }
        finally
        {
            Paths.EngineDir = previous.Item1;
            Paths.DataDir = previous.Item2;
            Directory.Delete(temporary, true);
        }
    }
}
