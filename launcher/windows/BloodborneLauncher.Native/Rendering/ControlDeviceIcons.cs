namespace BloodborneLauncher.Native.Rendering;

/// <summary>Use the launcher's monochrome Segoe symbols, without loading extra assets.</summary>
internal static class ControlDeviceIcons
{
    internal const string Mouse = "\uE962";
    internal const string Gamepad = "\uE7FC";
    internal const string Keyboard = "\uE765";

    internal static string Bindings(string device) => device == "controller" ? Gamepad : Keyboard;
}
