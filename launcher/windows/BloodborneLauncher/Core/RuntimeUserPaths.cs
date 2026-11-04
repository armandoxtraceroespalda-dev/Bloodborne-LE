using System.IO;
using System.Text.RegularExpressions;

namespace BloodborneLauncher.Core;

/// <summary>Resolve save and GPU roots before a child engine inherits the process environment.</summary>
public static class RuntimeUserPaths
{
    private static bool IsFormerEngineUser(string path)
    {
        string normalized = Environment.ExpandEnvironmentVariables(path).Replace('\\', '/').TrimEnd('/');
        return Regex.IsMatch(normalized, @"(?:^|/)BetterKBM-engine(?:-expanded|-update[0-9]+)?/user(?:/.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static void Resolve(IDictionary<string, string> environment, string dataDirectory, string saveDirectory, string? inheritedGpuDirectory)
    {
        // GPU caches are shared by player profiles; never put them in a profile's
        // savedata folder. A real custom cache path remains supported.
        environment.TryGetValue("BB_GPU_USER_DIR", out string? gpu);
        gpu ??= inheritedGpuDirectory;
        if (string.IsNullOrWhiteSpace(gpu) || IsFormerEngineUser(gpu))
            gpu = Path.Combine(dataDirectory, "user");
        environment["BB_GPU_USER_DIR"] = gpu;

        // Old Diagnostics > Extra variables can otherwise undo the save-folder
        // migration after BB_USER_DIR was assembled from the current settings.
        if (environment.TryGetValue("BB_USER_DIR", out string? user) && IsFormerEngineUser(user))
            environment["BB_USER_DIR"] = saveDirectory;
    }
}
