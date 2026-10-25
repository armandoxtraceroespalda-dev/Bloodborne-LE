using System.Runtime.InteropServices;
using System.Text;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    sealed record TextSpec(string Key, Rect Bounds, string Value, bool Secret, bool Output, Action<string>? Changed);
    sealed class TextEditor
    {
        public nint Window, Font;
        public int Height;
        public Rect Bounds;
        public bool Visible;
        public string Text = "";
        public bool Updating;
        public long RemovedOutputCharacters;
    }

    readonly Dictionary<string, TextSpec> _textSpecs = new();
    readonly Dictionary<string, TextEditor> _textEditors = new();
    readonly List<string> _staleTextEditors = new();
    readonly SubclassProc _textProc;
    nint _textBrush;
    bool _nativeTextNeedsSync = true, _nativeTextVisible;
    int _outputWheelRemainder;
    float _outputLineHeight = 16;
    bool _outputScrollRequested;
    delegate nint SubclassProc(nint h, uint msg, nint w, nint l, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    static extern bool SetWindowSubclass(nint h, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    static extern bool RemoveWindowSubclass(nint h, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")]
    static extern nint DefSubclassProc(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint SendMessage(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll", EntryPoint="SendMessageW", CharSet=CharSet.Unicode)]
    static extern nint SendTextMessage(nint h, uint msg, nint w, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetWindowText(nint h, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(nint h, StringBuilder text, int count);
    [DllImport("user32.dll")]
    static extern int GetWindowTextLength(nint h);
    [DllImport("user32.dll")]
    static extern nint GetFocus();
    [DllImport("user32.dll")]
    static extern short GetKeyState(int key);
    [DllImport("gdi32.dll")]
    static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]
    static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")]
    static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint precision, uint clip, uint quality, uint pitch, string face);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(nint h, string theme, string? names);
    [StructLayout(LayoutKind.Sequential)]
    struct MouseTracking
    {
        public uint Size, Flags;
        public nint Window;
        public uint Hover;
    }

    [DllImport("user32.dll")]
    static extern bool TrackMouseEvent(ref MouseTracking tracking);
    static void TrackLeave(nint h)
    {
        var tracking = new MouseTracking
        {
            Size = (uint)Marshal.SizeOf<MouseTracking>(),
            Flags = 2,
            Window = h
        };
        TrackMouseEvent(ref tracking);
    }

    void BeginNativeTextScene()
    {
        _textSpecs.Clear();
        _nativeTextNeedsSync = true;
    }

    void QueueNativeText(string key, Rect bounds, string value, bool secret, bool output, Action<string>? changed) => _textSpecs[key] = new(key, bounds, value, secret, output, changed);
    void FocusNativeInput(string key)
    {
        if(_textEditors.TryGetValue(key,out var editor)) W.SetFocus(editor.Window);
    }
    void HideNativeText()
    {
        foreach (var entry in _textEditors.Values)
        {
            W.ShowWindow(entry.Window, 0);
            entry.Visible = false;
        }

        _nativeTextNeedsSync = true;
    }

    void SyncNativeText()
    {
        if (_window == 0)
            return;
        bool visible = _switchAt < 0 && _popup == null && _recommendation == null && _resetPrompt == null && _messagePrompt == null && !_dialogClosing && _textCommit == null && _binding == null && _padCapture == null;
        if (!_nativeTextNeedsSync && _nativeTextVisible == visible)
            return;
        _nativeTextVisible = visible;
        _nativeTextNeedsSync = false;
        _staleTextEditors.Clear();
        foreach (var key in _textEditors.Keys)
            if (!_textSpecs.ContainsKey(key)) _staleTextEditors.Add(key);
        foreach (var key in _staleTextEditors)
        {
            DestroyText(_textEditors[key]);
            _textEditors.Remove(key);
        }

        _staleTextEditors.Clear();
        foreach (var spec in _textSpecs.Values)
        {
            if (!_textEditors.TryGetValue(spec.Key, out var editor))
            {
                // Multiline output wraps naturally: neither ES_AUTOHSCROLL nor WS_HSCROLL is set.
                uint style = 0x40010000u | (spec.Output ? 0x944u : 0x80u | (spec.Secret ? 0x20u : 0));
                nint h = W.CreateWindowEx(0, "EDIT", "", style, 0, 0, 1, 1, _window, 0, W.GetModuleHandle(null), 0);
                if (h == 0)
                    continue;
                editor = new TextEditor
                {
                    Window = h
                };
                _textEditors.Add(spec.Key, editor);
                SetWindowSubclass(h, _textProc, 1, 0);
                SetWindowTheme(h, "DarkMode_Explorer", null);
                if (spec.Key == GlobalSearchKey) SendTextMessage(h, 0x1501, 0, BloodborneLauncher.Core.UiText.T("Search settings..."));
                SendMessage(h, 0xC5, spec.Output ? Backend.LiveOutputBuffer.MaxCharacters + Backend.LiveOutputBuffer.MaxLines * 2 + 1024 : spec.Key == "online_username" ? 16 : spec.Key==GlobalSearchKey?120:65536, 0);
                if (spec.Secret)
                    SendMessage(h, 0xCC, 0x25cf, 0);
            }

            Rect b = ScreenRect(spec.Bounds, true);
            var pixels = new Rect((int)((b.X + (spec.Output ? 0 : 8 * _scale)) * _dpi / 96), (int)((b.Y + (spec.Output ? 0 : 5 * _scale)) * _dpi / 96), Math.Max(1, (int)((b.Width - (spec.Output ? 0 : 16 * _scale)) * _dpi / 96)), Math.Max(1, (int)((b.Height - (spec.Output ? 0 : 10 * _scale)) * _dpi / 96)));
            if (editor.Bounds != pixels)
            {
                W.SetWindowPos(editor.Window, 0, (int)pixels.X, (int)pixels.Y, (int)pixels.Width, (int)pixels.Height, 0x14);
                editor.Bounds = pixels;
            }

            int fontHeight = (int)Math.Round((spec.Output ? 13.125f : 18.375f) * _scale * _dpi / 96);
            if (editor.Height != fontHeight)
            {
                nint old = editor.Font;
                editor.Font = CreateFont(-fontHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, spec.Output ? "Consolas" : "Segoe UI");
                editor.Height = fontHeight;
                SendMessage(editor.Window, 0x30, editor.Font, 1);
                if (old != 0)
                    DeleteObject(old);
                if (spec.Output)
                    _outputLineHeight = TextLineHeight(editor) * 96 / _dpi;
            }

            bool changed = editor.Text != spec.Value;
            if (changed)
            {
                int first = (int)SendMessage(editor.Window, 0xCE, 0, 0);
                editor.Updating = true;
                try
                {
                    UpdateEditorText(editor, spec.Value, spec.Output, _output.RemovedCharacters);
                }
                finally
                {
                    editor.Updating = false;
                }

                editor.Text = spec.Value;
                SendMessage(editor.Window, 0xCD, 0, 0); // No retained undo history for programmatic output.
                if (spec.Output)
                {
                    int line = _followOutput ? (int)SendMessage(editor.Window, 0xBA, 0, 0) : first;
                    SendMessage(editor.Window, 0xB6, 0, line - (int)SendMessage(editor.Window, 0xCE, 0, 0));
                }
            }

            if (spec.Output)
            {
                if (_outputFollowPending && _followOutput)
                    SendMessage(editor.Window, 0xB6, 0, (int)SendMessage(editor.Window, 0xBA, 0, 0));
                else if (_outputScrollRequested)
                {
                    int first = (int)SendMessage(editor.Window, 0xCE, 0, 0);
                    SendMessage(editor.Window, 0xB6, 0, (int)(_outputOffset / _outputLineHeight) - first);
                }

                _outputFollowPending = _outputScrollRequested = false;
                ReadOutputScroll(editor);
            }

            bool show = visible && (spec.Key==GlobalSearchKey && _page==0 || b.Top >= _viewport.Top - 1 && b.Bottom <= _viewport.Bottom + 1);
            // The inactive search is painted with true alpha by Direct2D; the
            // native editor is needed only while typing, preserving IME support.
            if(spec.Key==GlobalSearchKey) show &= GlobalSearchFocused;
            if (editor.Visible != show)
            {
                W.ShowWindow(editor.Window, show ? 5 : 0);
                editor.Visible = show;
            }
        }
        if(_focusGlobalAfterSync && visible && _textEditors.TryGetValue(GlobalSearchKey,out var global) && global.Visible)
        { _focusGlobalAfterSync=false; W.SetFocus(global.Window); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, Average, Maximum, Weight, Overhang, AspectX, AspectY;
        public char First, Last, Default, Break;
        public byte Italic, Underlined, Strike, Pitch, Charset;
    }

    [DllImport("user32.dll")]
    static extern nint GetDC(nint h);
    [DllImport("user32.dll")]
    static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32.dll")]
    static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetTextMetrics(nint dc, out TextMetric metric);
    static int TextLineHeight(TextEditor editor)
    {
        nint dc = GetDC(editor.Window), old = SelectObject(dc, editor.Font);
        try
        {
            return GetTextMetrics(dc, out var metric) ? Math.Max(1, metric.Height) : editor.Height + 2;
        }
        finally
        {
            SelectObject(dc, old);
            ReleaseDC(editor.Window, dc);
        }
    }

    void ReadOutputScroll(TextEditor editor)
    {
        float oldOffset = _outputOffset, oldMax = _outputMax;
        int first = (int)SendMessage(editor.Window, 0xCE, 0, 0), lines = (int)SendMessage(editor.Window, 0xBA, 0, 0);
        float page = editor.Bounds.Height * 96 / _dpi;
        _outputMax = Math.Max(0, (lines - Math.Max(1, (int)(page / _outputLineHeight))) * _outputLineHeight);
        _outputOffset = first * _outputLineHeight;
        if (oldOffset != _outputOffset || oldMax != _outputMax)
        {
            _logMemory.Changed(_clock.Elapsed.TotalSeconds);
            InvalidateOutputThumb();
        }
    }

    void ScrollOutputWheelDelta(int delta)
    {
        _outputWheelRemainder += delta;
        int detents = _outputWheelRemainder / 120;
        _outputWheelRemainder %= 120;
        if (detents != 0)
            ScrollOutputLines(-detents * 3);
    }

    void ScrollOutputLines(int lines)
    {
        if (!_textEditors.TryGetValue("output-view", out var editor))
            return;
        SendMessage(editor.Window, 0xB6, 0, lines);
        ReadOutputScroll(editor);
    }

    void InvalidateOutputThumb()
    {
        var b = ScreenRect(_outputBox, true);
        Invalidate(new Rect(b.Right - 18 * _scale, b.Top, 18 * _scale, b.Height));
    }

    static unsafe void UpdateEditorText(TextEditor editor, string text, bool output, long removedCharacters)
    {
        if (!output)
        {
            SetWindowText(editor.Window, text);
            return;
        }
        int start = 0, end = 0;
        SendMessage(editor.Window, 0xB0, (nint)(&start), (nint)(&end));
        bool rolling = TextSplice.TryRollingAppend(editor.Text, text,
            removedCharacters - editor.RemovedOutputCharacters, out var drop, out var append);
        editor.RemovedOutputCharacters = removedCharacters;
        var patch = rolling ? append : TextSplice.Between(editor.Text, text);
        if (!rolling && patch.Removed == 0 && patch.Added == 0) return;
        // EDIT owns wrapping/selection. Replace only the changed range, without undo
        // snapshots or intermediate paints of a delete followed by an insertion.
        string replacement = text.Substring(patch.Start, patch.Added);
        SendMessage(editor.Window, 0xB, 0, 0); // WM_SETREDRAW
        try
        {
            if (rolling)
            {
                SendMessage(editor.Window, 0xB1, 0, drop.Removed);
                fixed (char* empty = "") SendMessage(editor.Window, 0xC2, 0, (nint)empty);
                start = drop.MapSelection(start);
                end = drop.MapSelection(end);
            }
            SendMessage(editor.Window, 0xB1, patch.Start, patch.Start + patch.Removed);
            fixed (char* value = replacement)
                SendMessage(editor.Window, 0xC2, 0, (nint)value); // EM_REPLACESEL, no undo
            SendMessage(editor.Window, 0xB1, patch.MapSelection(start), patch.MapSelection(end));
        }
        finally
        {
            SendMessage(editor.Window, 0xB, 1, 0);
            W.InvalidateRect(editor.Window, 0, false);
        }
    }

    bool NativeTextChanged(nint h, int code)
    {
        if (code != 0x300)
            return false;
        foreach (var pair in _textEditors)
        {
            var editor = pair.Value;
            if (editor.Window != h)
                continue;
            if (!editor.Updating && _textSpecs.TryGetValue(pair.Key, out var spec) && !spec.Output)
            {
                var buffer = new StringBuilder(GetWindowTextLength(h) + 1);
                GetWindowText(h, buffer, buffer.Capacity);
                editor.Text = buffer.ToString();
                _textSpecs[pair.Key] = spec with
                {
                    Value = editor.Text
                };
                spec.Changed?.Invoke(editor.Text);
            }

            return true;
        }

        return false;
    }

    nint NativeTextColor(nint dc, nint h)
    {
        // Color messages also arrive during native scrolling; do not allocate
        // a closure and LINQ enumerator for each repaint.
        bool owned = false;
        foreach (var editor in _textEditors.Values)
            if (editor.Window == h) { owned = true; break; }
        if (!owned) return 0;
        _textBrush = _textBrush == 0 ? CreateSolidBrush(0x151310) : _textBrush;
        SetTextColor(dc, 0xCBDDE3);
        SetBkColor(dc, 0x151310);
        return _textBrush;
    }

    nint TextWindowProc(nint h, uint message, nint w, nint l, nuint id, nuint data)
    {
        try
        {
            return TextWindowProcCore(h, message, w, l, id, data);
        }
        catch (Exception error)
        {
            _notice = error.GetBaseException().Message;
            Rebuild();
            return 0;
        }
    }

    nint TextWindowProcCore(nint h, uint message, nint w, nint l, nuint id, nuint data)
    {
        if(message==0x7)
        {
            foreach(var entry in _textEditors)
                if(entry.Value.Window==h && entry.Key!="output-view")
                { int hit=_hits.FindIndex(item=>item.Id==entry.Key);if(hit>=0)SetFocus(hit);break; }
        }
        if (_textEditors.TryGetValue(GlobalSearchKey, out var search) && search.Window == h)
        {
            if (message == 0x7) { _globalSearchActive = true; EnsureSettingsIndex(); Rebuild(); }
            if (message == 0x8) Rebuild();
            if (message == 0x100 && GlobalSearchKeyDown((int)w)) return 0;
        }
        if (message == 0x102 && w == 13)
            return 0;
        if (message == 0x200)
        {
            EndPadCue();
            ClearTooltip();
            UpdateHover(-1);
        }

        if (message == 0x100 && w == 0x41 && GetKeyState(0x11) < 0)
        {
            SendMessage(h, 0xB1, 0, -1);
            return 0;
        }

        bool outputWindow = _textEditors.TryGetValue("output-view", out var output) && output.Window == h;
        if (message == 0x20A && outputWindow)
        {
            ScrollOutputWheelDelta(unchecked((short)((long)w >> 16)));
            return 0;
        }

        var result = DefSubclassProc(h, message, w, l);
        if (outputWindow && message is 0x200 or 0x101 or 0x115)
        {
            ReadOutputScroll(output!);

        }

        return result;
    }

    void DestroyText(TextEditor editor)
    {
        RemoveWindowSubclass(editor.Window, _textProc, 1);
        W.DestroyWindow(editor.Window);
        if (editor.Font != 0)
            DeleteObject(editor.Font);
    }

    void DisposeNativeText()
    {
        foreach (var editor in _textEditors.Values)
            DestroyText(editor);
        _textEditors.Clear();
        _textSpecs.Clear();
        if (_textBrush != 0)
        {
            DeleteObject(_textBrush);
            _textBrush = 0;
        }
    }

    float ContentFade(double now)
    {
        if (_switchAt < 0)
            return 1;
        return _pending >= 0 ? _fadeFrom * (1 - (float)Math.Clamp((now - _switchAt) / .18, 0, 1)) : (float)Math.Clamp((now - _switchAt - .18) / .28, 0, 1);
    }

    float DimAmount(double now)
    {
        float t = (float)Math.Clamp((now - _dimAt) / .32, 0, 1);
        t = t * t * (3 - 2 * t);
        return _dimFrom + (_dimTarget - _dimFrom) * t;
    }

    async Task<bool> PrepareBackSoundAsync()
    {
        var token = _cancel.Token;
        string target = Path.Combine(_profile.Assets, "sounds", "back.wav");
        try
        {
            if (File.Exists(target))
            {
                using var input = File.OpenRead(target);
                if (BloodborneLauncher.Core.MenuWave.TryDuration(input, out _))
                    return true;
            }

            var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("--prepare-back-sound");
            info.ArgumentList.Add(_profile.Str("game_dir"));
            info.ArgumentList.Add(_profile.Assets);
            using var worker = System.Diagnostics.Process.Start(info);
            if (worker == null)
                return false;
            try
            {
                await worker.WaitForExitAsync(token);
            }
            finally
            {
                if (token.IsCancellationRequested && !worker.HasExited)
                    worker.Kill(true);
            }

            return worker.ExitCode == 0;
        }
        catch (Exception e)when (e is IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    Task<bool>? _backPreparation;
    bool _backPlayPending;
    void ReturnToStart()
    {
        if (_dialogClosing) return;
        if (_recommendation != null) { CloseRecommendations(); _audio.PlayBack(); return; }
        if (_messagePrompt != null) { FinishMessage(2); _audio.PlayBack(); return; }
        if (_resetPrompt != null) { CloseControlReset(false); _audio.PlayBack(); return; }
        if (_binding != null || _padCapture != null)
            return;
        _popup = null;
        _recommendation = null;
        _textCommit = null;
        _editing = null;
        _backPreparation ??= PrepareBackSoundAsync();
        if (_backPreparation is { IsCompleted: false })
            _backPlayPending = true;
        else
            _audio.PlayBack();
        Switch(0);
        Rebuild();
    }
}
