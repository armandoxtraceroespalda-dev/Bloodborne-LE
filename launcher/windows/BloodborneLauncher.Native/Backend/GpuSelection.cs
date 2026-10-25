using System.Text.RegularExpressions;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;
/// <summary>A deterministic model-family estimate, not a live benchmark. Keep Vulkan enumeration IDs intact.</summary>
internal static class GpuSelection
{
    public static GpuAdapter? Best(IEnumerable<GpuAdapter> devices) => devices.OrderByDescending(g => g.ApiVersion >= ((1u << 22) | (3u << 12))).ThenByDescending(g => g.Type == 2).ThenByDescending(Score).ThenByDescending(g => g.Memory).ThenBy(g => g.Index).FirstOrDefault();
    internal static double Score(GpuAdapter g)
    {
        string name = g.Name.ToUpperInvariant();
        var match = Regex.Match(name, @"(?:RTX|GTX|RX|ARC\s*[AB]?)\s*(\d{3,4})");
        int model = match.Success ? int.Parse(match.Groups[1].Value) : 0;
        double score = model switch
        {
            5090 => 1450,
            5080 => 1100,
            5070 => 760,
            5060 => 430,
            4090 => 1000,
            4080 => 820,
            4070 => 610,
            4060 => 370,
            3090 => 620,
            3080 => 555,
            3070 => 420,
            3060 => 290,
            3050 => 170,
            2080 => 350,
            2070 => 290,
            2060 => 230,
            1080 => 230,
            1070 => 175,
            9070 => 750,
            9060 => 410,
            7900 => 750,
            7800 => 565,
            7700 => 455,
            7600 => 290,
            6950 => 590,
            6900 => 570,
            6800 => 500,
            6700 => 360,
            6600 => 245,
            _ => Math.Min(250, g.Memory / 1073741824d * 12)};
        if (name.Contains("TI", StringComparison.Ordinal) || name.Contains("XTX", StringComparison.Ordinal))
            score *= 1.13;
        else if (name.Contains("SUPER", StringComparison.Ordinal) || name.Contains("XT", StringComparison.Ordinal))
            score *= 1.06;
        if (name.Contains("LAPTOP", StringComparison.Ordinal) || name.Contains("MOBILE", StringComparison.Ordinal))
            score *= .75;
        return score;
    }
}
