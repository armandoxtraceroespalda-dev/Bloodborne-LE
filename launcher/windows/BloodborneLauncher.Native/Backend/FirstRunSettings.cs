namespace BloodborneLauncher.Native.Backend;
public sealed partial class ProfileSession
{
    internal void RememberGameRoot(string game)
    {
        var latest=ReadJson(ConfigPath);latest["game_dir"]=game;
        AtomicJson(ConfigPath,latest);
        Settings["game_dir"]=game;_settingChanges.Remove("game_dir");
        InvalidateSettingsSnapshot();SavePreview();
    }
}
