using System.Globalization;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;

/// <summary>Display saved Start values without initializing the settings choice catalogs.</summary>
internal static class StartupSummary
{
    // These are display captions only. Upstream Choices still owns actual selectors
    // and settings validation; parity checks guard their current summary labels.
    public static string Upscaler(string value) => value switch
    {
        "fsr4" => "FSR 4",
        "fsr411" => "FSR 4.1.1",
        "fsr3" => "FSR 3",
        "dlss" => "DLSS",
        "taa" => "TAA",
        "off" => UiText.T("Off"),
        _ => value
    };

    public static string FrameRate(string value) => value == "uncap"
        ? UiText.T("Uncapped")
        : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rate) && rate > 0
            ? rate.ToString(CultureInfo.InvariantCulture) + " FPS" : value;

    public static (int W, int H) OutputSize(string value, int primaryWidth = 1920, int primaryHeight = 1080)
    {
        if (value == "auto")
            return primaryWidth >= 640 && primaryHeight >= 360
                ? (primaryWidth & ~1, primaryHeight & ~1) : (1920, 1080);
        var parts = value.Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height)
            ? (width, height) : (1920, 1080);
    }
}
