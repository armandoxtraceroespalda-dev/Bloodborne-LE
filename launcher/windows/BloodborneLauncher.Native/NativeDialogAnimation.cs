using BloodborneLauncher.Native.Interaction;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    const nuint DialogAnimationTimer = 3;
    readonly DialogFade _dialogFade = new();
    Action? _dialogAfterFade;
    bool _drawingDialog;
    bool _dialogClosing => _dialogAfterFade != null;

    void OpenDialogAnimation()
    {
        _dialogAfterFade = null;
        _dialogFade.Open(_clock.Elapsed.TotalSeconds);
        W.SetTimer(_window, DialogAnimationTimer, 16, 0);
    }

    void CloseDialogAnimation(Action complete)
    {
        if (_dialogClosing) return;
        ClearTooltip();
        _dialogAfterFade = complete;
        _dialogFade.Close(_clock.Elapsed.TotalSeconds);
        W.SetTimer(_window, DialogAnimationTimer, 16, 0);
        Invalidate();
    }

    void TickDialogAnimation()
    {
        if (_closed) return;
        if (_dialogFade.Complete(_clock.Elapsed.TotalSeconds))
        {
            W.KillTimer(_window, DialogAnimationTimer);
            var complete = _dialogAfterFade;
            _dialogAfterFade = null;
            if (complete != null) Run(complete);
        }
        // Only repaint opacity. Keep text layouts, textures and callbacks retained.
        Invalidate();
    }
}
