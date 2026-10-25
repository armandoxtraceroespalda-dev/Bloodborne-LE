using BloodborneLauncher.Core;
namespace BloodborneLauncher.Native.Backend;
internal static class PreferredGamepads
{
    public const string Key = "env:BB_GAMEPAD";
    public const string Help = "Automatic detects your gamepad. Preference applies next launch.";
    public static Option Option(IReadOnlyList<SdlPad.Device> devices, string saved)
    {
        var choices = new List<Choice> { new("Automatic", "") };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            string guid = device.Guid.Trim().ToLowerInvariant();
            if (!device.Supported || guid.Length != 32 || guid.All(c => c == '0') || !guid.All(Uri.IsHexDigit) || !seen.Add(guid)) continue;
            choices.Add(Choice.Literal(device.Name + " · " + guid, guid));
        }
        if (saved.Length > 0 && !choices.Any(c => c.Value.Equals(saved, StringComparison.OrdinalIgnoreCase)))
            choices.Add(Choice.Literal(UiText.Format("Saved preference: {0}", saved), saved));
        return new("", "Preferred gamepad", Key, OptionKind.Choice, true, choices, Default: "", Hint: Help);
    }
    public static string Label(IReadOnlyList<SdlPad.Device> devices, string saved) =>
        saved.Length == 0 ? UiText.T("Automatic") : devices.FirstOrDefault(d => d.Guid.Equals(saved, StringComparison.OrdinalIgnoreCase))?.Name ?? saved;
}
