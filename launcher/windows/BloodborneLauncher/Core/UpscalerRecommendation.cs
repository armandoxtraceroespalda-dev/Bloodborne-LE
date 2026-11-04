using System.IO;

namespace BloodborneLauncher.Core;

/// <summary>A conservative hardware recommendation, without loading an upscaler into the launcher.</summary>
public sealed record UpscalerRecommendation(string Value, string Reason)
{
    public string Label => Choices.Upscalers.First(c => c.Value == Value).Label;

    public static UpscalerRecommendation For(Hardware hardware, LauncherSettings settings,
        int outputWidth, int outputHeight, int preset)
    {
        // Match the game's environment precedence and working directory for custom model locations.
        string? EnvironmentValue(string key, string? gameValue = null)
        {
            string? value = gameValue ?? Environment.GetEnvironmentVariable(key);
            foreach (var item in settings.Str("extra_env").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = item.IndexOf('=');
                if (at > 0 && item[..at] == key) value = item[(at + 1)..];
            }
            return value;
        }

        if (hardware.Rtx)
        {
            var custom = ResolveDirectory(EnvironmentValue("BB_DLSS_DIR"));
            if ((custom != null && HasData(Path.Combine(custom, "nvngx_dlss.dll"))) ||
                HasData(Path.Combine(Paths.BinariesDir, "nvngx_dlss.dll")))
                return new("dlss", "DLSS is preferred for the selected RTX graphics card.");
            return new(OperatingSystem.IsWindows() ? "fsr" : "fsr3", "DLSS files are missing. FSR 3 is the compatible fallback.");
        }

        if (OperatingSystem.IsWindows()) return new("fsr", "AMD FSR SDK selects the best supported FSR implementation for the selected GPU.");

        // Keep experimental cross-vendor models available for manual selection. Automatic settings
        // use FSR 4 on RX 9000 only when this engine has the requested model; FSR 3 is built in.
        if (hardware.Rdna4)
        {
            var directory = ResolveDirectory(EnvironmentValue("BB_FSR4_DIR") is { Length: > 0 } custom ? custom : "fsr4_shaders");
            bool optimized = EnvironmentValue("BB_FSR4_OPT")?.StartsWith('0') != true;
            bool balanced = EnvironmentValue("BB_FSR4_BALANCED", settings.Bool("fsr4_balanced") ? "1" : null)?.StartsWith('1') == true && preset >= 3;
            if (directory != null && Fsr4ModelPresent(directory, outputWidth, outputHeight, preset, balanced, optimized))
                return new("fsr4", "FSR 4 is preferred for the selected Radeon RX 9000 graphics card.");
            return new("fsr3", "The FSR 4 model files for this resolution and quality are missing or incomplete. FSR 3 is recommended.");
        }
        return new("fsr3", "FSR 3 is the compatible default for the selected graphics card.");
    }

    private static string? ResolveDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        try { return Path.GetFullPath(directory, Path.GetFullPath(Paths.PortDir)); }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    private static bool HasData(string file)
    {
        try { return new FileInfo(file) is { Exists: true, Length: > 0 }; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    private static bool Fsr4ModelPresent(string directory, int width, int height, int preset, bool balanced, bool optimized)
    {
        if (width <= 0 || height <= 0) return false;
        string model = balanced ? "balanced" : Math.Clamp(preset, 0, 4) switch
        {
            0 => "native", 1 => "quality", 2 => "balanced", 3 => "performance", _ => "ultraperf"
        };
        string prefix = $"fsr4_model_v07_i8_{model}";
        string tier = width > 1920 || height > 1080 ? "2160" : "1080";
        bool Asset(string name, bool shader)
        {
            var file = Path.Combine(directory, name);
            var alternate = Path.Combine(directory, "opt", name);
            if (optimized && File.Exists(alternate)) file = alternate;
            if (!shader) return HasData(file);
            try
            {
                using var input = File.OpenRead(file);
                using var reader = new BinaryReader(input);
                return input.Length >= 20 && input.Length % 4 == 0 && reader.ReadUInt32() == 0x07230203;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
        }
        if (!Asset("rcas.spv", true) || !Asset("spd_auto_exposure.spv", true) ||
            !Asset($"{prefix}_initializers.bin", false) || !Asset($"{prefix}_pre_weights.bin", false) ||
            !Asset($"{prefix}_{tier}_pre.spv", true) || !Asset($"{prefix}_{tier}_post.spv", true)) return false;
        for (int pass = 1; pass <= 12; pass++)
            if (!Asset($"{prefix}_{tier}_pass{pass}.spv", true)) return false;
        return true;
    }
}
