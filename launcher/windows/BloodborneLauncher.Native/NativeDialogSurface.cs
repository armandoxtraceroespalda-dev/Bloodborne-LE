using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    int _dialogBaseOps, _dialogBaseHits, _dialogBaseGlows;
    bool _dialogBaseValid;

    void CaptureDialogBase()
    {
        _drawingDialog = false;
        _dialogBaseOps = _ops.Count;
        _dialogBaseHits = _hits.Count;
        _dialogBaseGlows = _glows.Count;
        _dialogBaseValid = _popup == null && _textCommit == null && _binding == null &&
            _padCapture == null && _editing == null && _notice.Length == 0;
    }

    void RefreshDialogSurface(bool lifecycle = true)
    {
        ClearTooltip();
        if (_dirty || !_dialogBaseValid || _switchAt >= 0)
            Rebuild();
        else
        {
            // Keep the retained page and its callbacks. Only modal operations and
            // hits are replaced, so opening/cancelling does not rebuild every row.
            _ops.RemoveRange(_dialogBaseOps, _ops.Count - _dialogBaseOps);
            _hits.RemoveRange(_dialogBaseHits, _hits.Count - _dialogBaseHits);
            _glows.RemoveRange(_dialogBaseGlows, _glows.Count - _dialogBaseGlows);
            _fixed = true;
            _scrollContent = false;
            _surface = false;
            _clip = null;
            _drawingDialog = false;
            if (_messagePrompt != null) DrawMessagePrompt();
            if (_recommendation != null) DrawRecommendations();
            if (_resetPrompt != null) DrawControlReset();
            _drawingDialog = false;
            SyncSceneResources();
            _nativeTextNeedsSync = true;
            Invalidate();
        }
        // Drop build-only measurements/temporary detection data. Actual scene
        // layouts remain owned by Canvas; collection runs only after Paint sync.
        if (lifecycle)
        {
            _canvas.ReleaseBuildMetrics();
            _navigationMemory.Request();
        }
    }

    Rect DialogArea => DialogPlacement.Book(_side, _width, _height);

    void DialogBackdrop(Rect box)
    {
        _drawingDialog = true;
        Fill(new Rect(0, 0, _width, _height), new Color4(0, 0, 0, .65f));
        Add(new(Ink.SoftSurface, box, new Color4(0, 0, 0, .75f), Image: "dialog-black"));
    }

    void DialogText(string text, Rect box, float size, Color4 color, bool translate = true) =>
        Add(new(Ink.Text, box, color, translate ? BloodborneLauncher.Core.UiText.T(text) : text,
            size * _scale, Center: true, Wrap: true));

    bool ModalTooltipAllowed => !_dialogClosing && _messagePrompt == null && (_resetPrompt == null ||
        _hover >= 0 && _hover < _hits.Count && _hits[_hover].Id == "controls-reset-info");
}
