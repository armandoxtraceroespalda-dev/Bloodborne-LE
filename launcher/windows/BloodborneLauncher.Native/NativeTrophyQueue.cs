using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Interaction;
using BloodborneLauncher.Native.Platform;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    const uint TrophyNoticeMessage = 0x8004;
    sealed record TrophyNotice(LauncherSettings Settings, Trophy? Trophy, bool Cheater, bool Preview);
    // Enough for the entire trophy catalog at once, plus synthetic notices.
    readonly NoticeQueue<TrophyNotice> _trophyNotices = new(64);
    TrophyBanner? _trophyBanner;
    bool _trophyNoticesDisposed;

    void QueueTrophy(LauncherSettings settings, Trophy? trophy = null, bool cheater = false, bool preview = false)
    {
        if (_closed || _trophyNoticesDisposed) return;
        // Repeated preview clicks must not crowd out real game notifications.
        if (preview && (_trophyBanner is { IsDisposed: false } || _trophyNotices.Contains(n => n.Preview))) return;
        if (!_trophyNotices.Enqueue(new(settings, trophy, cheater, preview)))
            LauncherDiagnostics.Record("Trophy notification queue is full; progress remains recorded in the trophy log.", LogLevel.Warn);
        AdvanceTrophyQueue();
    }

    void TrophyBannerClosed()
    {
        // Do not depend on the launcher's focus/animation timer while the game owns focus.
        if (!_closed && !_trophyNoticesDisposed && _window != 0)
            W.PostMessage(_window, TrophyNoticeMessage, 0, 0);
    }

    void AdvanceTrophyQueue()
    {
        if (_closed || _trophyNoticesDisposed || _trophyBanner is { IsDisposed: false }) return;
        _trophyBanner = null;
        while (_trophyNotices.TryTake(out var notice))
        {
            try
            {
                _trophyBanner = new TrophyBanner(_window, _profile.Assets, notice.Settings, notice.Trophy,
                    notice.Cheater, TrophyBannerClosed);
                return;
            }
            catch (Exception error)
            {
                // Notification presentation must never interrupt the game or its trophy tracking.
                LauncherDiagnostics.Record("Could not show trophy notification: " + error.Message, LogLevel.Warn);
            }
        }
    }

    void DisposeTrophyQueue()
    {
        _trophyNoticesDisposed = true;
        _trophyNotices.Clear();
        _trophyBanner?.Dispose();
        _trophyBanner = null;
    }
}
