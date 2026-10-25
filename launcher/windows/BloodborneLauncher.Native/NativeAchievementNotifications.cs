using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    void DrawAchievementNotifications(TrophyTracker tracker, LauncherSettings settings, float x, float width, ref float y)
    {
        PageGroup("Notification", x, width, ref y);
        var rows = PageCatalog.Build(6, _profile).OrderBy(o => o.Key switch
        {
            "trophy_notifications" => 0,
            "trophy_position" => 1,
            "trophy_sound" => 2,
            _ => 3
        }).ToArray();
        DrawReferenceRows(rows, x, width, ref y, true);
        y += 12 * _scale;
        bool two = width >= 760;
        float column = two ? (width - 24 * _scale) / 2 : width;
        float left = y, right = y;
        ReferenceCard("Sound file", "A sound of your own (WAV, MP3, MP4/M4A); empty uses the built-in chime", "\uE8D6", x, column, ref left, new[] { new PageButton("Choose…", "trophy-sound-file", ChooseTrophySound, "\uE8B7"), new PageButton("", "trophy-sound-reset", () =>
        {
            _profile.Set("trophy_sound_file", "", true);
            Rebuild();
        }, "\uE7A7") });
        if (!two) right = left;
        ReferenceCard("Try it", "Shows a sample notification and plays the sound", "\uE768", two ? x + column + 24 * _scale : x, column, ref right, new[] { new PageButton("Test", "trophy-notification-test", () =>
        {
            QueueTrophy(_profile.SourceSettings(), preview: true);
        }, "\uE735") });
        y = Math.Max(left, right);
        left = right = y;
        ReferenceCard("Achievement log", tracker.LogPath, "\uE8B7", x, column, ref left, new[] { new PageButton("", "trophy-log-folder", () => OpenPath(GameEnvironment.UserDir(settings), true), "\uE8A7") }, true);
        if (!two) right = left;
        ReferenceCard("Reset achievement progress", "Starts achievements and completion over, for a fresh run. The old log is kept as a backup", "\uE7A7", two ? x + column + 24 * _scale : x, column, ref right, new[] { new PageButton("Reset…", "trophy-progress-reset", () =>
        {
            if (Confirm(UiText.T("Reset achievement progress")))
            {
                tracker.Reset();
                Rebuild();
            }
        }, "\uE7A7") });
        y = Math.Max(left, right) + 16 * _scale;
    }
}
