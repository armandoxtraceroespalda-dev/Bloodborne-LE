using BloodborneLauncher.Native.Platform;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    const nuint BackgroundLogTimer = 2;
    bool _backgroundLogTimer;

    void PrepareBackgroundLog()
    {
        // The game can take focus before the automatic switch to Log has faded
        // in. Finish that one navigation without resuming background animation.
        if (_active || _game?.Running != true || _moreTab != 1 || _switchAt < 0 ||
            (_pending >= 0 ? _pending : _page) != 8 || W.IsIconic(_window)) return;
        if (_pending >= 0)
        {
            ReleasePageData();
            _page = _pending;
            _pending = -1;
            _scroll = 0;
        }
        _switchAt = -1;
        _dimFrom = _dimTarget;
        _dimAt = _clock.Elapsed.TotalSeconds - 1;
        _dirty = true;
        _navigationMemory.Request();
        Invalidate();
    }

    void UpdateBackgroundLogTimer()
    {
        bool needed = _window != 0 && !_closed && !_active && _game?.Running == true &&
            _page == 8 && _moreTab == 1 && _switchAt < 0 && !W.IsIconic(_window);
        if (needed == _backgroundLogTimer) return;
        _backgroundLogTimer = needed;
        if (needed) W.SetTimer(_window, BackgroundLogTimer, 500, 0);
        else W.KillTimer(_window, BackgroundLogTimer);
    }

    void RefreshBackgroundLog()
    {
        UpdateBackgroundLogTimer();
        if (!_backgroundLogTimer) return;
        double now = _clock.Elapsed.TotalSeconds;
        PollReferenceOutput(now);
        TryReclaimMemory(now);
    }

    void TryReclaimMemory(double now)
    {
        if (_logMemory.TakeRequest(now, _page == 8 && _moreTab == 1 && _switchAt < 0 &&
            !_dirty && !_nativeTextNeedsSync && _drag == null && (!_active || GetKeyState(1) >= 0)))
            _navigationMemory.Request();
        _navigationMemory.TryStart(_switchAt < 0 && !_dirty && !_nativeTextNeedsSync &&
            _drag == null && _popup == null && !_busy && !_dialogFade.Animating &&
            _binding == null && _padCapture == null && _textCommit == null && _editing == null && !_searchActive && !_globalSearchActive && !_menuGamepad.Busy && !_padScrolling);
    }
}
