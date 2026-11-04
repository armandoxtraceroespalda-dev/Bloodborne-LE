namespace BloodborneLauncher.Native.Rendering;

/// <summary>Segoe MDL2 glyphs, shared with the launcher's existing monochrome icons.</summary>
internal static class GraphicsSectionIcons
{
    public static string For(string title) => title switch
    {
        "Screen" => "\uE7F4",       // Display
        "Menus" => "\uE8F1",        // Reading mode
        "Game effects" => "\uE91B", // Photo
        "Picture" => "\uE790",      // Color palette
        "Upscaler" => "\uE740",     // Zoom
        "Lighting" => "\uE706",
        "Graphics profiles" => "\uE8B3", // Personalization
        "Frame generation" => "\uE8B9", // Multiple frames
        "HDR" => "\uE706",          // Brightness
        "Info" => "\uE946",         // Information
        "Experimental" => "",       // Deliberately undecorated
        _ => ""
    };
}
