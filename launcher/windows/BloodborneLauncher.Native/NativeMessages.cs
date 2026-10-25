using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    sealed record MessagePrompt(string Title, string Body, uint Buttons);
    MessagePrompt? _messagePrompt;
    int _messageResult;
    float _messageScroll, _messageScrollMax;

    // Existing confirmation callers remain synchronous, like MessageBox, but
    // dispatch Windows messages so painting, game output and cancellation continue.
    int ShowMessage(string body, string title, uint flags)
    {
        if (_messagePrompt != null || _closed) return 2;
        _messagePrompt = new(title, body, flags & 15);
        _messageResult = 0;
        OpenDialogAnimation();
        _messageScroll = 0;
        HideNativeText();
        RefreshDialogSurface();
        try
        {
            while (!_closed && _messageResult == 0)
            {
                int status = W.GetMessage(out var message, 0, 0, 0);
                if (status <= 0)
                {
                    if (status == 0) W.PostQuitMessage((int)message.WParam);
                    break;
                }
                W.TranslateMessage(ref message);
                W.DispatchMessage(ref message);
            }
            return _messageResult == 0 ? 2 : _messageResult;
        }
        finally
        {
            _messagePrompt = null;
            _messageScroll = _messageScrollMax = 0;
            if (!_closed) RefreshDialogSurface();
        }
    }

    void FinishMessage(int result) => CloseDialogAnimation(() => _messageResult = result);

    void DrawMessagePrompt()
    {
        var prompt = _messagePrompt!;
        _fixed = true;
        _scrollContent = false;
        _surface = false;
        _clip = null;
        float width = Math.Min(640 * _scale, DialogArea.Width - 48);
        float textHeight = WrappedHeight(prompt.Body, 18, width - 72 * _scale, false, true);
        float height = Math.Min(DialogArea.Height - 48, Math.Max(214 * _scale, textHeight + 142 * _scale));
        var box = DialogPlacement.Center(DialogArea, width, height);
        DialogBackdrop(box);
        DialogText(prompt.Title, new Rect(box.X + 30 * _scale, box.Y + 20 * _scale, width - 60 * _scale, 42 * _scale), 23, Text, false);
        var viewport = new Rect(box.X + 36 * _scale, box.Y + 70 * _scale, width - 72 * _scale, Math.Max(1, height - 140 * _scale));
        _messageScrollMax = Math.Max(0, textHeight - viewport.Height);
        _messageScroll = Math.Clamp(_messageScroll, 0, _messageScrollMax);
        _clip = viewport;
        DialogText(prompt.Body, new Rect(viewport.X, viewport.Y - _messageScroll, viewport.Width, Math.Max(viewport.Height, textHeight)), 18, Gold, false);
        _clip = null;
        var labels = prompt.Buttons switch
        {
            3 => new[] { ("Yes", 6), ("No", 7), ("Cancel", 2) },
            4 => new[] { ("Yes", 6), ("No", 7) },
            _ => new[] { ("OK", 1) }
        };
        float button = Math.Min(160 * _scale, (width - 64 * _scale) / labels.Length - 12 * _scale);
        float start = box.Center.X - (labels.Length * button + (labels.Length - 1) * 12 * _scale) / 2;
        for (int i = 0; i < labels.Length; i++)
        {
            var (label, result) = labels[i];
            Button(label, new Rect(start + i * (button + 12 * _scale), box.Bottom - 58 * _scale, button, 38 * _scale),
                "message-" + result, () => FinishMessage(result));
        }
    }

    void MessageMouse(float x, float y, bool down)
    {
        int hit = FindHit(x, y);
        bool button = hit >= 0 && _hits[hit].Id.StartsWith("message-", StringComparison.Ordinal);
        UpdateHover(button ? hit : -1);
        if (button && down) { _audio.Play(MenuSoundCue.Change); _hits[hit].Action?.Invoke(0); }
    }

    void ScrollMessage(float pixels)
    {
        float next = Math.Clamp(_messageScroll + pixels, 0, _messageScrollMax);
        if (_dialogClosing || next == _messageScroll) return;
        _messageScroll = next;
        RefreshDialogSurface(lifecycle: false);
    }

    void MessageKey(int key)
    {
        // Enter/Escape cancel destructive confirmations unless an explicit button
        // is clicked; informational notices accept Enter as usual.
        if (key == 0x1B) FinishMessage(_messagePrompt!.Buttons == 0 ? 1 : _messagePrompt.Buttons == 4 ? 7 : 2);
        else if (key == 0x0D) FinishMessage(_messagePrompt!.Buttons == 0 ? 1 : _messagePrompt.Buttons == 4 ? 7 : 2);
        else if (key is 0x26 or 0x28 or 0x21 or 0x22)
            ScrollMessage((key is 0x26 or 0x21 ? -1 : 1) * (key is 0x21 or 0x22 ? 180 : 48) * _scale);
    }
}
