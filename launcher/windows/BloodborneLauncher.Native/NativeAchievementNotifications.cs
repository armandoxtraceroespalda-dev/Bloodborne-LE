using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Rect = Vortice.Mathematics.Rect;

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
        float column = (width - 24 * _scale) / 2;
        float soundWidth = DrawNotificationAction(new("Sound file", "trophy-sound-file", ChooseTrophySound, "\uE8D6"),
            x, y, column - 44 * _scale, _profile.Str("trophy_sound_file", UiText.T("A sound of your own (WAV, MP3, MP4/M4A); empty uses the built-in chime")));
        DrawNotificationAction(new("", "trophy-sound-reset", () =>
        {
            _profile.Set("trophy_sound_file", "", true);
            Rebuild();
        }, "\uE7A7"), x + soundWidth + 6 * _scale, y, 36 * _scale);
        DrawNotificationAction(new("Try it", "trophy-notification-test",
            () => QueueTrophy(_profile.SourceSettings(), preview: true), "\uE768"),
            x + column + 24 * _scale, y, column, UiText.T("Shows a sample notification and plays the sound"));
        y += 50 * _scale;
        DrawNotificationAction(new("Achievement log", "trophy-log-folder", () =>
            OpenPath(System.IO.File.Exists(tracker.LogPath) ? tracker.LogPath : GameEnvironment.UserDir(settings)), "\uE8B7"),
            x, y, column, tracker.LogPath);
        DrawNotificationAction(new("Reset achievements progress", "trophy-progress-reset", () =>
        {
            if (Confirm(UiText.T("Reset achievements progress")))
            {
                tracker.Reset();
                Rebuild();
            }
        }, "\uE7A7"), x + column + 24 * _scale, y, column,
            UiText.T("Starts achievements and completion over, for a fresh run. The old log is kept as a backup"));
        y += 54 * _scale;
        FinishSectionOutline();
    }

    float DrawNotificationAction(PageButton action, float x, float y, float width, string hint = "")
    {
        float font = ReferenceLayout.ActionFont;
        float textWidth = _canvas.Measure(UiText.T(action.Caption), font * _scale).X;
        float buttonWidth = Math.Min(Math.Max(1, width), Math.Max(36 * _scale, textWidth + 48 * _scale));
        if (textWidth > buttonWidth - 44 * _scale && action.Caption.Length > 0)
            font *= Math.Max(1, buttonWidth - 44 * _scale) / Math.Max(1, textWidth);
        int hit = _hits.Count;
        Button(action.Caption, new Rect(x, y + 7 * _scale, buttonWidth, 38 * _scale), action.Key,
            () => Run(action.Click), font: font, icon: action.Icon);
        if (hint.Length > 0) _hits[hit] = _hits[hit] with { Hint = hint };
        return buttonWidth;
    }
}
