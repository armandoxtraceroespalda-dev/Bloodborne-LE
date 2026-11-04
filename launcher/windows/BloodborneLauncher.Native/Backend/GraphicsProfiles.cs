using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;

/// <summary>Stable visual profile identities and a persistent, data-only Custom snapshot.</summary>
internal static class GraphicsProfiles
{
    internal const string Custom = "-1";
    internal static string Id(Looks.Look look)
    {
        for (int i = 0; i < Looks.Presets.Count; i++)
            if (ReferenceEquals(look, Looks.Presets[i])) return "builtin:" + i.ToString(CultureInfo.InvariantCulture);
        return "saved:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(look.Name)))[..12].ToLowerInvariant();
    }
    static readonly string[] BuiltInNames = ["Original look", "Performance", "Better shadows", "BB Remastered"];
    static readonly string[] BuiltInHints = ["The game's own lighting and picture, with none of the extra effects. Your baseline, costs nothing extra.", "Turns off shadows from dynamic lights and motion blur and shortens shadow distance, so lamps stop casting moving shadows. How much it saves has not been measured.", "Better ambient occlusion, contact shadows and softer sun shadows, without the extra light. Its cost has not been measured.", "Nearly everything on: indirect light, volumetrics, sharper reflections and soft shadows. The heaviest choice; its cost has not been measured, so expect a large drop on older GPUs. An effect your GPU cannot run turns itself off."];
    internal static Choice[] Choices() => new[] { new Choice("Custom", Custom, "Restores your saved custom graphics settings.") }
        .Concat(Looks.Presets.Select((look, i) => new Choice(BuiltInNames[i], Id(look),
            i == 2 ? UiText.T("Contact shadows") + " + " + UiText.T("Soft sun shadows (PCSS)") : BuiltInHints[i])))
        .Concat(Looks.Saved().Select(look => new Choice(look.Name, Id(look),
            look.Description.Length > 0 ? look.Description : "Applies the graphics settings stored in this profile.", TranslateLabel: false))).ToArray();

    internal static Looks.Look? Resolve(string value)
    {
        var profiles = Looks.Presets.Concat(Looks.Saved()).ToArray();
        // Version 115 stored list positions; accept them without changing the user's values.
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            return index >= 0 && index < profiles.Length ? profiles[index] : null;
        return profiles.FirstOrDefault(look => Id(look) == value);
    }
    internal static string Selection(ProfileSession profile)
    {
        var stored = profile.Str("native-look", Custom);
        if (stored == Custom) return Custom;
        var look = Resolve(stored);
        return look != null && Looks.IsCurrent(look, profile.Ini) ? Id(look) : Custom;
    }
    static string Snapshot(ProfileSession profile) => Path.Combine(Path.GetDirectoryName(profile.SandboxIni)!, "graphics-custom.json");

    internal static void CaptureCustom(ProfileSession profile)
    {
        var values = new JsonObject();
        foreach (var knob in Looks.Knobs) values[knob.Key] = profile.Ini.Raw(knob.Key);
        string file = Snapshot(profile), staging = file + ".saving";
        File.WriteAllText(staging, new JsonObject { ["version"] = 1, ["values"] = values }.ToJsonString());
        File.Move(staging, file, overwrite: true);
    }
    static JsonObject? ReadCustom(ProfileSession profile)
    {
        string file = Snapshot(profile);
        // Preserve the pre-preset values captured by version 115 when upgrading.
        if (!File.Exists(file))
        {
            string legacy = Path.Combine(Path.GetDirectoryName(profile.SandboxIni)!, "looks-previous.json");
            if (File.Exists(legacy)) File.Copy(legacy, file);
        }
        if (!File.Exists(file)) return null;
        var root = JsonNode.Parse(File.ReadAllText(file));
        if (root?["values"] is not JsonObject values)
            throw new InvalidDataException(UiText.T("The saved settings are damaged."));
        return values;
    }
    internal static bool Apply(ProfileSession profile, string value, bool preserveCustom = false)
    {
        if (value == Custom)
        {
            var values = ReadCustom(profile);
            if (values == null) return false;
            // Validate the complete snapshot before changing any setting.
            var restored = new Dictionary<string, string?>();
            foreach (var knob in Looks.Knobs)
            {
                var item = values[knob.Key];
                string? raw = item == null ? null : item.GetValue<string>();
                if (raw != null && Looks.Normal(knob, raw) == null)
                    throw new InvalidDataException(UiText.T("The saved settings are damaged."));
                restored[knob.Key] = raw;
            }
            profile.EditSource((settings, ini) =>
            {
                foreach (var pair in restored) ini.Set(pair.Key, pair.Value);
            });
            profile.Set("native-look", Custom, true);
            return true;
        }
        var look = Resolve(value);
        if (look == null) return false;
        if (!preserveCustom && Selection(profile) == Custom) CaptureCustom(profile);
        else if (!File.Exists(Snapshot(profile)) && ReadCustom(profile) == null) CaptureCustom(profile);
        profile.EditSource((settings, ini) => Looks.Apply(look, ini));
        profile.Set("native-look", Id(look), true);
        return true;
    }
}
