namespace BloodborneLauncher.Native.Rendering;

/// <summary>Resolve heading icons before translation; explicit existing icons always win.</summary>
internal static class SectionIcons
{
    public static string For(int page, string title, string existing = "")
    {
        if (existing.Length > 0) return existing;
        if (page == 0) return "";
        if (page == 1) return GraphicsSectionIcons.For(title);
        return title switch
        {
            "Language" => "\uE8F2",
            "Keybindings" or "Input" or "Movement" => "\uE765",
            "Gameplay Tweaks" => "\uE713",
            "Items" => "\uE7B8",
            "Character" or "Player" => "\uE77B",
            "Blood Echoes" => "\uE734",
            "Play" or "Launch" => "\uE768",
            "Server" => "\uE968",
            "The Hunter's Dream account" or "Hunter's Dream account" or "shadNet account" => "\uE77B",
            "Connection" or "Online" => "\uE774",
            "All achievements" => "\uE735",
            "Notification" => "\uE7ED",
            "Bloodborne PC" or "Graphics" => "\uE7F4",
            "Performance" => "\uE9D9",
            "Saves" => "\uE74E",
            "Files" => "\uE8B7",
            "Diagnostics" or "Diagnostics and logs" => "\uE9D9",
            "About" => "\uE946",
            "Mods" => "\uEA86",
            "Third-party patches" or "Patches" => "\uE70F",
            "Game output" or "Logs" => "\uE8A5",
            "Sound" => "\uE767",
            "Memory and VRAM" => "\uE950",
            "Upscalers" or "Direct rendering" => "\uE740",
            "Engine preset" => "\uE713",
            "Game fixes" => "\uE90F",
            _ => page == 8 && title.Length > 0 ? "\uE713" : ""
        };
    }
}
