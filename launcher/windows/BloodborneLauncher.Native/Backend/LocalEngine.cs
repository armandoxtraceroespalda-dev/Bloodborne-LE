using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;
internal static class LocalEngine
{
    internal static string Select(string packageRoot,string configured)
    {
        string bundled=Path.Combine(packageRoot,"engine");
        return Paths.IsPortDir(bundled)?bundled:configured;
    }
}
