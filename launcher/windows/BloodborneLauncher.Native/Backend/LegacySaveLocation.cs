using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BloodborneLauncher.Native.Backend;

// Only the old automatically generated engine folders qualify. Arbitrary custom
// save folders remain valid and must never be redirected by an engine update.
internal static class LegacySaveLocation
{
    internal static bool IsLegacy(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var directory = new DirectoryInfo(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
        return directory.Name.Equals("user", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(directory.Parent?.Name ?? "", @"^BetterKBM-engine(?:-expanded|-update[0-9]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string? Migrate(string configured, string destination)
    {
        if (!IsLegacy(configured)) return null;
        string source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured)).TrimEnd(Path.DirectorySeparatorChar);
        destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(destination, comparison)) return null;
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, comparison) ||
            source.StartsWith(destination + Path.DirectorySeparatorChar, comparison))
            throw new IOException("The new saves folder must be separate from the old saves folder.");
        if (!Directory.Exists(source)) return destination;
        RejectLink(source);
        // Check every collision before copying anything. Different saves are never
        // overwritten, regardless of which file is newer.
        var pending = new List<(string Source, string Target)>();
        Inspect(source, destination, pending);
        foreach (var file in pending)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
            // Publish complete files only; an interrupted copy can be retried safely.
            string temporary = file.Target + ".migration-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(file.Source, temporary, false);
                if (!SameContents(file.Source, temporary)) throw new IOException("Save copy verification failed: " + file.Source);
                File.Move(temporary, file.Target, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return destination;
    }

    static void Inspect(string source, string destination, List<(string Source, string Target)> pending)
    {
        if (File.Exists(destination)) throw new IOException("A file occupies the new saves folder: " + destination);
        if (Directory.Exists(destination)) RejectLink(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            RejectLink(file);
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (Directory.Exists(target)) throw new IOException("A folder occupies the new save file: " + target);
            if (File.Exists(target))
            {
                RejectLink(target);
                if (!SameContents(file, target)) throw new IOException("Different files exist in both saves folders; neither was overwritten: " + target);
            }
            else pending.Add((file, target));
        }
        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            RejectLink(folder);
            Inspect(folder, Path.Combine(destination, Path.GetFileName(folder)), pending);
        }
    }

    static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Save-folder migration cannot follow links: " + path);
    }

    static bool SameContents(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
}
