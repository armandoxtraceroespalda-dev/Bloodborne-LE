using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class LocalEngineTests
{
    internal static void Run(string temporary)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message);}
        string package=Path.Combine(temporary,"local-engine-package"),engine=Path.Combine(package,"engine");
        Directory.CreateDirectory(Path.Combine(engine,"out"));
        Check(LocalEngine.Select(package,"previous-engine")=="previous-engine","unbuilt local source does not masquerade as a usable engine");
        string extension=OperatingSystem.IsWindows()?".exe":"";
        File.WriteAllText(Path.Combine(engine,"out","bb-probe"+extension),"fixture");
        Check(LocalEngine.Select(package,"previous-engine")=="previous-engine","incomplete local engine keeps the existing fallback");
        File.WriteAllText(Path.Combine(engine,"out","bb-start"+extension),"fixture");
        Check(LocalEngine.Select(package,"previous-engine")==engine,"ready engine beside the root launcher takes precedence over the old external installation");
        Check(LocalEngine.Select(package,"")==engine && LocalEngine.Select(package,engine)==engine,"fresh and repeated launches resolve the same local engine folder");
    }
}
