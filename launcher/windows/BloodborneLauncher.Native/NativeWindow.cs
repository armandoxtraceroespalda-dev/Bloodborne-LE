using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using BloodborneLauncher.Native.Rendering;
using BloodborneLauncher.Native.Interaction;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow : IDisposable
{
    readonly string _gameVersion, _achievements;
    readonly MenuAudio _audio;
    readonly ProfileSession _profile;
    readonly Canvas _canvas;
    IReadOnlyList<Option>? _graphicsOptions;
    IReadOnlyList<Option> GraphicsOptions => _graphicsOptions ??= GraphicsSchema.Build();
    readonly W.WindowProc _proc;
    readonly List<Op> _ops = new(800);
    readonly List<Hit> _hits = new(180);
    readonly List<Op> _glows = new(20);
    readonly List<Op> _tooltipOps = new(8);
    readonly List<Op> _renderOps = new(900);
    List<Op>? _overlayTarget;
    Rect? _tooltipBounds;
    readonly PointerState _pointer = new();
    int _hover => _pointer.Hover;

    int _focus => _pointer.Focus;

    double _hoverSince => _pointer.Since;

    bool _tooltipShown { get => _pointer.TooltipShown; set => _pointer.TooltipShown = value; }

    readonly NavigationMemoryReclaimer _navigationMemory = new();
    bool _startupCleanupRequested, _autoPlay, _directPlay;
    NativeGameSession? _game;
    long _sceneBuilds;
    float _mouseX = -1, _mouseY = -1;
    readonly string _report;
    readonly bool _benchmark, _diagnostics;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    readonly List<object> _samples = new();
    nint _window;
    float _dimFrom, _dimTarget, _fadeFrom = 1, _scrollDragStart, _scrollDragY;
    double _dimAt;
    float _width = 1280, _height = 800, _dpi = 96, _scale = 1, _side = 220, _scroll, _scrollMax;
    Rect _viewport;
    bool _dirty = true, _active = true, _muted, _closed, _fixed = true;
    Rect? _clip;
    bool _scrollContent, _surface, _bookTransition;
    int _page, _pending = -1;
    double _switchAt = -1, _lastSample, _lastBenchmark, _lastStressHover;
    Hit? _drag;
    Popup? _popup;
    Option? _editing;
    string _editText = "";
    int _popupOffset;
    string _notice = "";
    sealed record Hit(Rect Bounds, string Id, Action<float>? Action = null, string Hint = "", Option? Option = null, bool Enabled = true, Rect? Clip = null, bool Scrollable = false);
    sealed record Popup(Option Option, Rect Anchor);
    static readonly Color4 Gold = new(212 / 255f, 196 / 255f, 157 / 255f, 1), Text = new(227 / 255f, 221 / 255f, 203 / 255f, 1), Muted = new(173 / 255f, 154 / 255f, 114 / 255f, 1);
    public NativeWindow(ProfileSession profile, string[] args)
    {
        _profile = profile;
        _directPlay = _autoPlay = args.Contains("--play");
        _cheats.Restore(profile.Str("cheats_unlocked") == "true");
        _canvas = new(profile.Assets);
        _proc = WndProc;
        _textProc = TextWindowProc;
        _benchmark = args.Contains("--benchmark");
        int n = Array.IndexOf(args, "--report");
        _diagnostics = _benchmark || n >= 0;
        _report = n >= 0 && n + 1 < args.Length ? args[n + 1] : Path.Combine(AppContext.BaseDirectory, "LauncherApp", "native-preview-metrics.json");
        _muted = profile.Str("menu_sound_muted") == "true";
        _audio = new(profile.Assets, _muted);
        _gameVersion = GameVersion();
        _achievements = AchievementSnapshot();
    }

    public int Run()
    {
        var instance = W.GetModuleHandle(null);
        var wc = new W.WindowClass
        {
            Size = (uint)Marshal.SizeOf<W.WindowClass>(),
            Proc = _proc,
            Instance = instance,
            Cursor = W.LoadCursor(0, 32512),
            Name = "BloodborneNativePreview"
        };
        if (W.RegisterClassEx(ref wc) == 0)
            throw new System.ComponentModel.Win32Exception();
        _window = W.CreateWindowEx(0x00040000, wc.Name!, "Bloodborne Launcher", 0x820F0000, unchecked((int)0x80000000), unchecked((int)0x80000000), 1280, 800, 0, 0, instance, 0);
        if (_window == 0)
            throw new System.ComponentModel.Win32Exception();
        int dark = 1;
        W.DwmSetWindowAttribute(_window, 20, ref dark, 4);
        int noBorder = unchecked((int)0xfffffffe);
        W.DwmSetWindowAttribute(_window, 34, ref noBorder, 4); // DWMWA_BORDER_COLOR / DWMWA_COLOR_NONE
        W.SetWindowPos(_window, 0, 0, 0, 0, 0, 0x37);
        W.ShowWindow(_window, 1);
        Resize();
        W.SetTimer(_window, 1, 33, 0);
        while (W.GetMessage(out var m, 0, 0, 0) > 0)
        {
            W.TranslateMessage(ref m);
            W.DispatchMessage(ref m);
        }

        return 0;
    }

    nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        try
        {
            switch (msg)
            {
                case 0x0020: // WM_SETCURSOR
                    if (((long)lp & 0xffff) == 1)
                    {
                        int hit = FindHit(_mouseX, _mouseY);
                        int cursor = hit >= 0 && _hits[hit].Id == "binding-search" ? 32513 : hit >= 0 && _hits[hit].Enabled && _hits[hit].Option?.Kind != OptionKind.Slider && _hits[hit].Id != "scroll" ? 32649 : 32512;
                        W.SetCursor(SharedCursor(cursor));
                        return 1;
                    }

                    break;
                case 0x0111:
                    if (NativeTextChanged(lp, (int)(((long)wp >> 16) & 0xffff)))
                        return 0;
                    break;
                case 0x0133:
                case 0x0138:
                    var brush = NativeTextColor(wp, lp);
                    if (brush != 0)
                        return brush;
                    break;
                case 0x8002:
                    ReturnToStart();
                    return 0;
                case TrophyNoticeMessage:
                    AdvanceTrophyQueue();
                    return 0;
                case GameEventsMessage:
                    PollGameEvents();
                    return 0;
                case WorkerMessage:
                    CompleteWorker();
                    return 0;
                // Own the non-client area in both activation states. DefWindowProc
                // would repaint the inactive WS_THICKFRAME using a white system frame.
                case 0x0083:
                    return 0; // WM_NCCALCSIZE, including wParam == 0
                case 0x0085:
                    return 0; // WM_NCPAINT
                case 0x0086:
                    return 1; // WM_NCACTIVATE; preserve normal activation
                case 0x0084:
                {
                    var p = new W.Point
                    {
                        X = unchecked((short)(long)lp),
                        Y = unchecked((short)((long)lp >> 16))
                    };
                    W.ScreenToClient(h, ref p);
                    float x = p.X * 96 / _dpi, y = p.Y * 96 / _dpi;
                    float edge = 7;
                    if (!W.IsZoomed(h))
                    {
                        bool l = x < edge, r = x > _width - edge, t = y < edge, b = y > _height - edge;
                        if (t && l)
                            return 13;
                        if (t && r)
                            return 14;
                        if (b && l)
                            return 16;
                        if (b && r)
                            return 17;
                        if (l)
                            return 10;
                        if (r)
                            return 11;
                        if (t)
                            return 12;
                        if (b)
                            return 15;
                    }

                    if (y < 30 && x < _width - 145)
                        return 2;
                    return 1;
                }

                case 0x0024:
                {
                    var mm = Marshal.PtrToStructure<W.MinMax>(lp);
                    mm.MinTrack = new W.Point
                    {
                        X = (int)(900 * _dpi / 96),
                        Y = (int)(620 * _dpi / 96)
                    };
                    Marshal.StructureToPtr(mm, lp, false);
                    return 0;
                }

                case 0x0005:
                    if (_window != 0 && !W.IsIconic(h))
                        Resize();
                    PrepareBackgroundLog();
                    UpdateBackgroundLogTimer();
                    return 0;
                case 0x02E0:
                {
                    var r = Marshal.PtrToStructure<W.Rect>(lp);
                    W.SetWindowPos(h, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x14);
                    Resize();
                    return 0;
                }

                case 0x001C:
                    _active = wp != 0;
                    if (_active)
                    {
                        W.SetTimer(h, 1, 33, 0);
                        Invalidate();
                    }
                    else
                    {
                        W.KillTimer(h, 1);
                        StopLauncherPad();
                    }
                    PrepareBackgroundLog();
                    UpdateBackgroundLogTimer();
                    return 0;
                case 0x0219: // WM_DEVICECHANGE
                    _nextPadProbe=0; SdlPad.RefreshDevices();
                    return 0;
                case 0x0014:
                    return 1;
                case 0x000F:
                {
                    W.BeginPaint(h, out var p);
                    try
                    {
                        if (_window != 0)
                            Paint(new Rect(p.Area.Left * 96 / _dpi, p.Area.Top * 96 / _dpi, (p.Area.Right - p.Area.Left) * 96 / _dpi, (p.Area.Bottom - p.Area.Top) * 96 / _dpi));
                    }
                    finally
                    {
                        W.EndPaint(h, ref p);
                    }

                    return 0;
                }

                case 0x02A3:
                    ClearTooltip();
                    UpdateHover(-1);
                    return 0;
                case 0x0200:
                    TrackLeave(h);
                    Mouse(unchecked((short)(long)lp) * 96 / _dpi, unchecked((short)((long)lp >> 16)) * 96 / _dpi, false);
                    return 0;
                case 0x0201:
                    Mouse(unchecked((short)(long)lp) * 96 / _dpi, unchecked((short)((long)lp >> 16)) * 96 / _dpi, true);
                    return 0;
                case 0x0202:
                    if (_binding != null)
                    {
                        CaptureAtom("Mouse1", false);
                        return 0;
                    }

                    if (_drag != null)
                    {
                        FlushSliderDrag();
                        _drag = null;
                        W.ReleaseCapture();
                        _profile.SavePreview();
                    }

                    return 0;
                case 0x0215:
                    FlushSliderDrag();
                    _drag = null;
                    return 0;
                case 0x0204:
                case 0x0207:
                case 0x020B:
                    if (_binding != null)
                    {
                        CaptureAtom(msg == 0x0204 ? "Mouse3" : msg == 0x0207 ? "Mouse2" : ((long)wp >> 16) == 1 ? "Mouse4" : "Mouse5", true);
                        return 0;
                    }

                    if (msg == 0x0204)
                    {
                        ReturnToStart();
                        return 0;
                    }

                    break;
                case 0x0205:
                case 0x0208:
                case 0x020C:
                    if (_binding != null)
                    {
                        CaptureAtom(msg == 0x0205 ? "Mouse3" : msg == 0x0208 ? "Mouse2" : ((long)wp >> 16) == 1 ? "Mouse4" : "Mouse5", false);
                        return 0;
                    }

                    break;
                case 0x020A:
                    if (_dialogClosing) return 0;
                    if (_messagePrompt != null) { ScrollMessage(-((short)((long)wp >> 16)) / 120f * 48 * _scale); return 0; }
                    if (_resetPrompt != null) return 0;
                    if (_recommendation != null)
                    {
                        ScrollRecommendations(-unchecked((short)((long)wp >> 16)) / 120f * 48 * _scale);
                        return 0;
                    }
                    var wheelPoint = new W.Point
                    {
                        X = unchecked((short)(long)lp),
                        Y = unchecked((short)((long)lp >> 16))
                    };
                    W.ScreenToClient(h, ref wheelPoint);
                    _mouseX = wheelPoint.X * 96 / _dpi;
                    _mouseY = wheelPoint.Y * 96 / _dpi;
                    if (_binding != null)
                    {
                        string wheel = unchecked((short)((long)wp >> 16)) > 0 ? "WheelUp" : "WheelDown";
                        CaptureAtom(wheel, true);
                        CaptureAtom(wheel, false);
                        return 0;
                    }

                    if (_popup != null)
                    {
                        _popupOffset = Math.Clamp(_popupOffset - unchecked((short)((long)wp >> 16)) / 120, 0, Math.Max(0, (_popup.Option.Choices?.Count ?? 0) - (_popup.Option.Key == "display_device" ? 1 : 8)));
                        Rebuild();
                        return 0;
                    }

                    if (ScrollOutputWheel(unchecked((short)((long)wp >> 16)) / 120f))
                        return 0;
                    if (_page != 0)
                        SetScroll(_scroll - unchecked((short)((long)wp >> 16)) / 120f * 80 * _scale);
                    return 0;
                case 0x0102:
                    if (HandleText((char)(int)wp))
                        return 0;
                    if (_editing != null)
                    {
                        char ch = (char)(int)wp;
                        if (ch == '\b')
                        {
                            if (_editText.Length > 0)
                                _editText = _editText[..^1];
                        }
                        else if (!char.IsControl(ch) && _editText.Length < 24)
                            _editText += ch;
                        Rebuild();
                    }

                    return 0;
                case 0x0100:
                case 0x0104:
                    if (CaptureKey((int)wp, true, lp))
                        return 0;
                    Key((int)wp);
                    return 0;
                case 0x0101:
                case 0x0105:
                    if (CaptureKey((int)wp, false, lp))
                        return 0;
                    break;
                case 0x0113:
                    if ((nuint)wp == DialogAnimationTimer) { TickDialogAnimation(); return 0; }
                    if ((nuint)wp == BackgroundLogTimer) { RefreshBackgroundLog(); return 0; }
                    Tick();
                    return 0;
                case 0x0010:
                    RequestClose();
                    return 0;
                case 0x0002:
                    _closed = true;
                    DisposeTrophyQueue();
                    W.KillTimer(h, 1);
                    W.KillTimer(h, BackgroundLogTimer);
                    W.KillTimer(h, DialogAnimationTimer);
                    SaveReport();
                    W.PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception e)
        {
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,"LauncherApp"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "LauncherApp", "native-preview-error.txt"), e.ToString());
            // Last-resort crash reporting cannot rely on a renderer that just failed.
            W.MessageBox(h, e.GetBaseException().Message, "Bloodborne Launcher", 0x10);
            if (_window != 0)
                W.DestroyWindow(h);
        }

        return W.DefWindowProc(h, msg, wp, lp);
    }

    void Resize()
    {
        if (_closed)
            return;
        W.GetClientRect(_window, out var r);
        _dpi = W.GetDpiForWindow(_window);
        if (_dpi == 0)
            _dpi = 96;
        _width = (r.Right - r.Left) * 96 / _dpi;
        _height = (r.Bottom - r.Top) * 96 / _dpi;
        _scale = Math.Clamp(Math.Min(_width / 1280, _height / 800), .85f, 1.3f);
        float navScale = Math.Clamp(Math.Min(_width / 1280, _height / 800), .85f, 1.6f);
        _side = 220 * navScale;
        foreach (var title in PageCatalog.Titles.Where(t => t != "Start"))
            _side = Math.Max(_side, _canvas.Measure(UiText.T(title), 26 * navScale).X + 36 * navScale);
        _canvas.Resize(_window, r.Right - r.Left, r.Bottom - r.Top, _dpi);
        _popup = null;
        _scroll = 0;
        Rebuild();
    }

    Rect ScreenRect(Rect b, bool scrollable) => ScrollGeometry.Screen(b, scrollable, _scroll);
    void SetScroll(float value)
    {
        float next = Math.Clamp(value, 0, _scrollMax);
        if (next == _scroll)
            return;
        _scroll = next;
        _canvas.UpdateViewport(_renderOps, _scroll);
        _nativeTextNeedsSync = true;
        ClearTooltip();
        SyncNativeText();
        UpdateHover(FindHit(_mouseX, _mouseY));
        Invalidate(new Rect(_viewport.X - 4, _viewport.Y - 42, _width - _viewport.X + 4, _viewport.Height + 50));
    }

    void Underline(Rect b)
    {
        Add(new(Ink.Image, new Rect(b.X, b.Bottom - 9, b.Width / 2, 10), new Color4(1, 1, 1, 1), Image: "menu-underline", FlipY: true));
        Add(new(Ink.Image, new Rect(b.X + b.Width / 2, b.Bottom - 9, b.Width / 2, 10), new Color4(1, 1, 1, 1), Image: "menu-underline", Flip: true, FlipY: true));
    }

    void ControlFrame(Rect b, int trigger = -1, bool selected = false, bool enabled = true)
    {
        Add(new(Ink.ControlSurface, b, new Color4(1, 1, 1, enabled ? 1 : .4f)));
        if (trigger >= 0 || selected)
            _glows.Add(Add(new(Ink.Glow, b, new Color4(1, 1, 1, enabled ? .8f : .3f), Trigger: trigger, Selected: selected)));
        Underline(b);
    }

    void Rebuild()
    {
        _dirty = true;
        Invalidate();
    }

    void Invalidate()
    {
        if (_window != 0)
            W.InvalidateRect(_window, 0, false);
    }

    void Invalidate(Rect area)
    {
        var r = new W.Rect
        {
            Left = (int)MathF.Floor(area.Left * _dpi / 96),
            Top = (int)MathF.Floor(area.Top * _dpi / 96),
            Right = (int)MathF.Ceiling(area.Right * _dpi / 96),
            Bottom = (int)MathF.Ceiling(area.Bottom * _dpi / 96)
        };
        W.InvalidateArea(_window, ref r, false);
    }

    void Tick()
    {
        PollGameEvents();
        UpdateBackgroundLogTimer();
        if (!_active || _closed || W.IsIconic(_window))
            return;
        double now = _clock.Elapsed.TotalSeconds;
        if (_backPlayPending && _backPreparation is { IsCompleted: true })
        {
            _backPlayPending = false;
            _audio.PlayBack();
        }

        FlushSliderDrag();
        PollReferencePad();
        PollLauncherPad(now);
        PollReferenceOutput(now);
        if (_switchAt >= 0)
        {
            if (_pending >= 0 && now - _switchAt >= .18)
            {
                ReleasePageData();
                _page = _pending;
                _pending = -1;
                _scroll = 0;
                _popup = null;
                _dirty = true;
            }

            if (now - _switchAt >= .46)
            {
                _switchAt = -1;
                _navigationMemory.Request();
            }
            Invalidate();
        }
        else
            foreach (var glow in _glows)
                if (glow.Pulse && glow.VisibleFor(_hover, _focus))
                    Invalidate(ScreenRect(glow.Bounds, glow.Scrollable));
        if (_drag==null && ModalTooltipAllowed && _popup == null && _textCommit == null && _binding == null && _editing == null && !_tooltipShown && _hover >= 0 && _hover < _hits.Count && _hits[_hover].Hint.Length > 0 && now - _hoverSince > .65)
        {
            _tooltipShown = true;
            BuildTooltip();
        }

        if (now - _dimAt < .35)
            Invalidate();
        if (_autoPlay && _startupCleanupRequested && !_dirty && _switchAt < 0)
        {
            _autoPlay = false;
            Run(StartOrStopGame);
        }
        TryReclaimMemory(now);
        if (_diagnostics && now - _lastSample >= 2)
        {
            _lastSample = now;
            var m = new W.Memory
            {
                Size = (uint)Marshal.SizeOf<W.Memory>()
            };
            W.GetProcessMemoryInfo(W.GetCurrentProcess(), ref m, m.Size);
            _samples.Add(new { seconds = now, page = PageCatalog.Titles[_page], privateBytes = (ulong)m.Private, settingsSnapshotLoads = _profile.SettingsSnapshotLoads, settingsClones = _profile.SettingsClones, workingSet = (ulong)m.Working, startupCleanupRequested = _startupCleanupRequested, graphicsPagePrepared = _graphicsOptions != null, helpCatalogLoaded = _profile.HelpLoaded, navigationCollections = _navigationMemory.Collections, navigationCollectionMs = _navigationMemory.CollectionMilliseconds, navigationWorkingSetTrims = _navigationMemory.WorkingSetTrims, navigationReclaimFailures = _navigationMemory.Failures, managedBytes = GC.GetTotalMemory(false), allocatedBytes = GC.GetTotalAllocatedBytes(false), images = _canvas.ImageCount, imageBytes = _canvas.ImageBytes, textFormats = _canvas.TextFormatCount, textMeasurements = _canvas.MeasurementCount, renderOps = _renderOps.Count, nativeEditors = _textEditors.Count, textLayouts = _canvas.TextLayoutCount, layoutsCreated = _canvas.LayoutsCreated, layoutsReleased = _canvas.LayoutsReleased, sceneBuilds = _sceneBuilds, sliderRowUpdates = _sliderRowUpdates, scenePrepares = _canvas.ScenePrepares, pointerChanges = _pointer.Changes, frames = _canvas.Frames });
            if (_samples.Count > 1800)
                _samples.RemoveAt(0);
        }

        if (_benchmark)
        {
            int desired = now < 4 ? 0 : now < 12 ? 1 : now < 16 ? 3 : now < 20 ? 4 : now < 24 ? 2 : now < 28 ? 6 : now < 32 ? 7 : now < 36 ? 8 : now < 40 ? 5 : 0;
            if (_page != desired && _switchAt < 0)
                Switch(desired);
            else if (_page != 0 && _switchAt < 0 && now - _lastBenchmark >= 1.5)
            {
                _lastBenchmark = now;
                SetScroll(_scroll >= _scrollMax ? 0 : _scroll + _viewport.Height * .7f);
            }

            // Exercise hover without moving the user's physical mouse or
            // clicking settings. A brief hold also exercises tooltip lifetime.
            if (_page != 0 && _switchAt < 0 && now - _lastStressHover >= .033 && now % 5 > 1)
            {
                _lastStressHover = now;
                for (int count = 1; count <= _hits.Count; count++)
                {
                    int index = (_hover + count) % _hits.Count;
                    if (_hits[index].Option != null && _hits[index].Enabled && Canvas.Intersects(ScreenRect(_hits[index].Bounds, _hits[index].Scrollable), _viewport))
                    {
                        UpdateHover(index);
                        break;
                    }
                }
            }

            if (now >= 44)
            {
                SaveReport();
                W.DestroyWindow(_window);
            }
        }
    }

    void ReleasePageData()
    {
        _pageRows = null;
        _graphicsOptions = null;
        _settingsIndex = null;
        _globalResults = Array.Empty<SettingResult>();
        _logLines = null;
        _trophies = null;
        _modEntries = null;
        _patchEntries = null;
        _patchEnabled = null;
        _restoreBackups = null;
        _preferredPads = null;
        _importSources = null;
        _dialogText = "";
        _output.ReleaseSnapshot();
        _logMemory.Cancel();
        _canvas.ReleaseBuildMetrics();
        _trimPageStorage = true;
    }

    bool _trimPageStorage;
    void TrimPageStorage()
    {
        if (!_trimPageStorage) return;
        _trimPageStorage = false;
        _ops.TrimExcess();
        _hits.TrimExcess();
        _glows.TrimExcess();
        _renderOps.TrimExcess();
    }

    void Switch(int page)
    {
        if (page == (_pending >= 0 ? _pending : _page))
            return;
        FlushSliderDrag();
        if(page!=0)_globalSearchActive=false;
        _navigationMemory.CancelPending();
        if (_searchNotice) { _notice = ""; _searchNotice = false; }
        double now = _clock.Elapsed.TotalSeconds;
        _fadeFrom = ContentFade(now);
        _dimFrom = DimAmount(now);
        _dimTarget = page == 0 ? 0 : 1;
        _dimAt = now;
        _drag = null;
        W.ReleaseCapture();
        ClearTooltip();
        HideNativeText();
        _pending = page;
        _bookTransition = _page == 0 || page == 0;
        _switchAt = _clock.Elapsed.TotalSeconds;
        Invalidate();
    }

    void Paint(Rect dirty)
    {
        if (!_canvas.HasTarget)
            Resize();
        if (_dirty)
        {
            Build();
            _dirty = false;
        }

        float fade = 1;
        if (_switchAt >= 0)
        {
            double elapsed = _clock.Elapsed.TotalSeconds - _switchAt;
            fade = _pending >= 0 ? _fadeFrom * (1 - (float)Math.Clamp(elapsed / .18, 0, 1)) : (float)Math.Clamp((elapsed - .18) / .28, 0, 1);
        }

        RevealSettingResult();
        SyncNativeText();
        UpdateBackgroundLogTimer();
        _canvas.Paint(_renderOps, dirty, fade, (float)_clock.Elapsed.TotalSeconds, _hover, _focus, _scroll, _scrollMax, _bookTransition ? fade : 1, DimAmount(_clock.Elapsed.TotalSeconds), _outputOffset, _outputMax, _dialogFade.Value(_clock.Elapsed.TotalSeconds), navigationFocus:PadFocusRect,
            navigationOpacity:(_messagePrompt!=null || _resetPrompt!=null || _recommendation!=null)?_dialogFade.Value(_clock.Elapsed.TotalSeconds):1);
        // Queue startup reclamation only after the first valid frame. The timer
        // uses the same scene/interaction gates as navigation cleanup.
        if (!_startupCleanupRequested && _canvas.HasTarget && _canvas.Frames > 0)
        {
            _startupCleanupRequested = true;
            _navigationMemory.Request();
        }
    }

    Op Add(Op op)
    {
        var prepared = op with
        {
            Fixed = _fixed,
            Clip = _clip,
            Scrollable = _scrollContent,
            Surface = _surface,
            Dialog = _drawingDialog
        };
        (_overlayTarget ?? _ops).Add(prepared);
        return prepared;
    }

    void Fill(Rect b, Color4 c, float round = 0) => Add(new(round > 0 ? Ink.Round : Ink.Fill, b, c, Radius: round));
    void TextAt(string t, Rect b, float size = 18, bool center = false, Color4? color = null, bool detail = false, bool bold = false, bool translate = true, int weight = 0) => Add(new(Ink.Text, b, color ?? Text, translate ? UiText.T(t) : t, size * _scale, Center: center, Detail: detail, Bold: bold, Weight: weight));
    void Line(float x, float y, float width, Color4? color = null) => Add(new(Ink.Line, new Rect(x, y, width, 1), color ?? Gold));
    void Glow(Rect b, int trigger, bool selected = false)
    {
        _glows.Add(Add(new(Ink.Glow, b, new Color4(1, 1, 1, 1), Image: "menu-glow-half-joined", Pulse: true, Trigger: trigger, Selected: selected)));
    }

    void SyncSceneResources()
    {
        _renderOps.Clear();
        _renderOps.AddRange(_ops);
        _renderOps.AddRange(_tooltipOps);
        _canvas.PrepareScene(_renderOps, _scroll);
    }

    void ClearTooltip()
    {
        _tooltipShown = false;
        if (_tooltipOps.Count == 0)
            return;
        if (_tooltipBounds is Rect bounds)
            Invalidate(bounds);
        _tooltipOps.Clear();
        _tooltipBounds = null;
        SyncSceneResources();
    }

    void InvalidateInteraction(int index, bool focus = false)
    {
        if (index < 0 || index >= _hits.Count)
            return;
        foreach (var glow in _glows)
            if (glow.Trigger == index)
                Invalidate(ScreenRect(glow.Bounds, glow.Scrollable));
        // Every control needs a repaint on hover; static frames and slider grips
        // intentionally have no animation clock to invalidate them later.
        {
            var b = ScreenRect(_hits[index].Bounds, _hits[index].Scrollable);
            Invalidate(new Rect(b.X - 4, b.Y - 4, b.Width + 8, b.Height + 8));
        }
    }

    void SetFocus(int index)
    {
        if (_focus == index)
            return;
        if(PadFocusRect is Rect oldPad)Invalidate(oldPad);
        int previous = _focus;
        _pointer.Focus = index;
        if(PadFocusRect is Rect newPad)Invalidate(newPad);
        InvalidateInteraction(previous, true);
        InvalidateInteraction(index, true);
    }

    int FindHit(float x, float y)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            var hit = _hits[i];
            if (ScrollGeometry.Hit(hit.Bounds, hit.Scrollable, hit.Clip, x, y, _scroll))
                return i;
        }

        return -1;
    }

    void UpdateHover(int index)
    {
        int previous = _hover;
        if (!_pointer.Move(index, _clock.Elapsed.TotalSeconds))
            return;
        ClearTooltip();
        InvalidateInteraction(previous);
        InvalidateInteraction(index);
        if (index >= 0 && _hits[index].Id is not ("min" or "max" or "close" or "mute" or "output-text" or "output-scroll" or "output-scroll-x" or "scroll"))
            _audio.Play(MenuSoundCue.Hover);
    }

    int HitAt(Rect b, string id, Action<float> action, string hint = "", Option? option = null, bool enabled = true)
    {
        if (_sliderHitBase>=0)
        {
            _sliderHits.Add(new(b,id,action,option?.Kind==OptionKind.Choice?"":hint,option,enabled,_clip,_scrollContent));
            return _sliderHitBase+_sliderHits.Count-1;
        }
        _hits.Add(new(b, id, action, option?.Kind == OptionKind.Choice ? "" : hint, option, enabled, _clip, _scrollContent));
        return _hits.Count - 1;
    }

    void Button(string label, Rect b, string id, Action action, bool active = false, bool detail = false, float font = ReferenceLayout.ActionFont, bool enabled = true, string icon = "")
    {
        int index = _hits.Count;
        HitAt(b, id, _ => action(), enabled: enabled);
        bool caption = id is "min" or "max" or "close" or "mute";
        if (id == "play")
        {
            Add(new(Ink.Shadow, b, new Color4(0, 0, 0, .8f)));
            Glow(new Rect(b.X - 32 * _scale, b.Y + 16 * _scale, b.Width + 64 * _scale, Math.Max(1, b.Height - 32 * _scale)), index);
        }
        else if (caption)
        {
            if (id != "mute")
                Add(new(Ink.Fill, b, id == "close" ? new Color4(.77f, .17f, .11f, 1) : new Color4(.2f, .21f, .22f, 1), Trigger: index));
        }
        else
            ControlFrame(b, index, active, enabled);
        if (caption)
        {
            string glyph = id switch
            {
                "min" => "\uE921",
                "max" => W.IsZoomed(_window) ? "\uE923" : "\uE922",
                "close" => "\uE8BB",
                _ => "\uE767"
            };
            Symbol(glyph, b, id == "mute" ? 18 : 10, enabled ? Text : Muted);
            if (id == "mute" && _muted)
                TextAt("×", new Rect(b.Right - 12, b.Bottom - 14, 12, 12), 10, true, detail: true);
        }
        else if (icon.Length > 0)
        {
            float textWidth = _canvas.Measure(UiText.T(label), font * _scale).X;
            float left = b.X + (b.Width - textWidth - 24 * _scale) / 2;
            Symbol(icon, new Rect(left, b.Y, 18 * _scale, b.Height), 18, enabled ? Text : Muted);
            TextAt(label, new Rect(left + 24 * _scale, b.Y, textWidth, b.Height), font, true, color: enabled ? Text : Muted);
        }
        else
            TextAt(label, b, font, true, color: enabled ? Text : Muted, detail: detail);
    }

    void Build()
    {
        _drawingDialog = false;
        BeginNativeTextScene();
        _canvas.BeginLayout();
        _sceneBuilds++;
        _tooltipShown = false;
        _tooltipOps.Clear();
        _tooltipBounds = null;
        _ops.Clear();
        _sliderRows.Clear();
        _hits.Clear();
        _glows.Clear();
        _clip = null;
        _scrollContent = false;
        _fixed = true;
        var wallpaperArea=DialogPlacement.Book(_side,_width,_height);
        _canvas.ImageCover(_ops, new Rect(0, 0, _width, _height), "background",
            centerX:wallpaperArea.X+wallpaperArea.Width/2, centerY:wallpaperArea.Y+wallpaperArea.Height/2);
        if (_ops.Count > 0)
            _ops[^1] = _ops[^1] with
            {
                Fixed = true
            };
        Add(new(Ink.Dim, new Rect(0, 0, _width, _height), new Color4(0, 0, 0, .8611765f)));
        Button("—", new Rect(_width - 138, 0, 46, 34), "min", () => W.ShowWindow(_window, 6), detail: true);
        Button(W.IsZoomed(_window) ? "❐" : "□", new Rect(_width - 92, 0, 46, 34), "max", () => W.ShowWindow(_window, W.IsZoomed(_window) ? 9 : 3), detail: true);
        Button("×", new Rect(_width - 46, 0, 46, 34), "close", RequestClose, detail: true);
        // Keep page identifiers stable; only the sidebar order changes when cheats are unlocked.
        int[] tabs = _cheats.Unlocked ? new[]
        {
            0,
            2,
            1,
            3,
            4,
            9,
            5,
            6,
            7,
            8
        }

        : new[]
        {
            0,
            2,
            1,
            3,
            4,
            5,
            6,
            7,
            8
        };
        float row = (_height - 52 * _scale) / (tabs.Length + .8f), y = 26 * _scale;
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = tabs[i];
            float h = row * (i == 0 ? 1.8f : 1);
            var b = new Rect(0, y, _side, h);
            int index = _hits.Count;
            HitAt(b, "nav" + tab, _ =>
            {
                _subpage = "";
                _moreTab = 0;
                Switch(tab);
            });
            Add(new(Ink.Shadow, new Rect(12 * _scale, y + 8, _side - 24 * _scale, h - 16), new Color4(0, 0, 0, .68f)));
            Glow(new Rect(-_side * .25f, y, _side * 1.5f, h), index, tab == _page);
            TextAt(PageCatalog.Titles[tab], b, i == 0 ? 48 : 26, true, bold: i == 0);
            y += h;
        }

        Button(_muted ? "× ♪" : "♪", new Rect(_width - 46, _height - 42, 34, 32), "mute", () =>
        {
            _muted = !_muted;
            _audio.Muted = _muted;
            _profile.Set("menu_sound_muted", _muted ? "true" : "false", true);
            Rebuild();
        }, detail: true);
        _fixed = false;
        if (_page == 0)
            Home();
        else
            Graphics();
        _clip = null;
        DrawGlobalSearchResults();
        CaptureDialogBase();
        if (_popup != null)
            DrawPopup();
        if (_recommendation != null)
            DrawRecommendations();
        if (_resetPrompt != null)
            DrawControlReset();
        if (_textCommit != null || _binding != null || _padCapture != null)
            DrawEditor();
        if (_editing != null)
        {
            var b = new Rect(_side + 60, _height / 2 - 80, _width - _side - 120, 160);
            Fill(b, new Color4(.025f, .03f, .033f, 1), 12);
            TextAt("Output resolution", new Rect(b.X + 16, b.Y + 10, b.Width - 32, 30), 22, true);
            TextAt(_editText + "|", new Rect(b.X + 16, b.Y + 52, b.Width - 32, 42), 24, true, Gold, true);
            TextAt("Width x Height · Enter: apply · Esc: cancel", new Rect(b.X + 16, b.Y + 110, b.Width - 32, 30), 14, true, Muted, true);
        }

        if (_notice.Length > 0)
        {
            Fill(new Rect(_side + 24, _height - 82, _width - _side - 100, 28), new Color4(0, 0, 0, .85f), 8);
            TextAt(_notice, new Rect(_side + 32, _height - 82, _width - _side - 116, 28), 14, color: Gold, detail: true);
        }

        if (_messagePrompt != null) DrawMessagePrompt();
        _drawingDialog = false;
        _pointer.Move(FindHit(_mouseX, _mouseY), _clock.Elapsed.TotalSeconds);
        SyncSceneResources();
        TrimPageStorage();
    }

    void BuildTooltip()
    {
        if (_tooltipBounds is Rect previous)
            Invalidate(previous);
        _tooltipOps.Clear();
        _tooltipBounds = null;
        _overlayTarget = _tooltipOps;
        bool wasFixed = _fixed;
        bool wasScroll = _scrollContent;
        _scrollContent = false;
        var oldClip = _clip;
        _fixed = true;
        _clip = null;
        try
        {
            if (_tooltipShown && _hover >= 0 && _hover < _hits.Count && _hits[_hover].Hint.Length > 0)
            {
                var hit = _hits[_hover];
                string tip = UiText.T(hit.Hint);
                float width = Math.Min(560 * _scale, _width - 40), height = Math.Min(160, 34 + tip.Length / 55 * 20) * _scale;
                var anchor = ScreenRect(hit.Bounds, hit.Scrollable);
                var box = new Rect(Math.Clamp(anchor.X, 14, _width - width - 14), Math.Clamp(anchor.Bottom + 8, 36, _height - height - 54), width, height);
                _tooltipBounds = new Rect(box.X - 8, box.Y - 8, box.Width + 16, box.Height + 16);
                for (int i = 4; i >= 0; i--)
                    Fill(new Rect(box.X - i * 2, box.Y - i * 2, box.Width + i * 4, box.Height + i * 4), new Color4(0, 0, 0, i == 0 ? .9f : .06f), 12 + i * 2);
                Add(new(Ink.Text, new Rect(box.X + 14, box.Y + 8, box.Width - 28, box.Height - 16), Gold, tip, 14 * _scale, Detail: true, Wrap: true));
            }
        }
        finally
        {
            _overlayTarget = null;
            _fixed = wasFixed;
            _scrollContent = wasScroll;
            _clip = oldClip;
        }

        SyncSceneResources();
        if (_tooltipBounds is Rect current)
            Invalidate(current);
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    void Home()
    {
        float x = _side + 24, paneWidth = _width - _side - 48, paneHeight = _height - 48;
        _scroll=0;_scrollMax=0;_viewport=new Rect(x,34,paneWidth,paneHeight);
        var labels = new[]
        {
            "Upscaler",
            "Output",
            "Frame rate",
            "Achievements"
        };
        string output = _profile.Ini.Get("output_res", "1920x1080");
        var(w, h) = output == "auto"
            ? StartupSummary.OutputSize(output, GetSystemMetrics(0), GetSystemMetrics(1))
            : StartupSummary.OutputSize(output);
        var values = new[]
        {
            StartupSummary.Upscaler(_profile.Ini.Get("upscaler", "fsr4")),
            $"{w} × {h}",
            StartupSummary.FrameRate(_profile.Str("fps_mode", "60")),
            _achievements
        };
        var widths = new float[4];
        float contentWidth = 0;
        for (int i = 0; i < 4; i++)
        {
            widths[i] = Math.Max(_canvas.Measure(UiText.T(labels[i]), 11.375f * _scale).X, _canvas.Measure(values[i], 16.625f * _scale).X) + 16 * _scale;
            contentWidth += widths[i];
        }

        float searchWidth = Math.Min(290 * _scale, Math.Max(150 * _scale, paneWidth * .29f));
        float overviewLimit = Math.Max(140 * _scale, paneWidth - searchWidth - 18 * _scale);
        float infoScale = Math.Min(1, overviewLimit / Math.Max(1, contentWidth + 24 * _scale));
        for (int i = 0; i < 4; i++) widths[i] *= infoScale;
        float overviewWidth = Math.Min(overviewLimit, contentWidth + 24 * _scale), overviewHeight = 68 * _scale;
        var overview = new Rect(x + paneWidth - overviewWidth, 24 + 12 * _scale, overviewWidth, overviewHeight);
        Add(new(Ink.SoftSurface, overview, new Color4(1, 1, 1, .42f), Image: "soft-surface"));
        DrawGlobalSearch(new Rect(x, overview.Y + 15 * _scale, Math.Min(searchWidth, Math.Max(100, overview.X - x - 18 * _scale)), 38 * _scale));
        float cx = overview.X + 12 * _scale * infoScale;
        for (int i = 0; i < 4; i++)
        {
            TextAt(labels[i], new Rect(cx, overview.Y + 12 * _scale, widths[i], 18 * _scale), 11.375f * infoScale, true, Muted);
            TextAt(values[i], new Rect(cx, overview.Y + 34 * _scale, widths[i], 24 * _scale), 16.625f * infoScale, true, translate: false);
            cx += widths[i];
        }

        var version = new Rect(x, _height - 24 - 106 * _scale, 260 * _scale, 98 * _scale);
        Add(new(Ink.SoftSurface, version, new Color4(1, 1, 1, .42f), Image: "soft-surface"));
        TextAt("Game version", new Rect(version.X + 16 * _scale, version.Y + 28 * _scale, version.Width - 32 * _scale, 18 * _scale), 11.375f, true, Muted);
        TextAt(_gameVersion, new Rect(version.X + 16 * _scale, version.Y + 50 * _scale, version.Width - 32 * _scale, 24 * _scale), 16.625f, true, translate: false);
        float areaHeight = Math.Max(190, paneHeight - overviewHeight - 12 * _scale), availableHeight = Math.Max(60, areaHeight / 3 * 1.12f - 20), availableWidth = Math.Max(100, paneWidth * .448f - 12);
        float zoom = Math.Min(availableWidth / 280, availableHeight / 190), pw = 280 * zoom, ph = 190 * zoom;
        var play = new Rect(x + paneWidth - 12 - pw, _height - 24 - 20 - ph, pw, ph);
        Button(_game?.Running == true ? "Stop" : "Play", play, "play", () => Run(StartOrStopGame), font: 50.75f * zoom / _scale);
    }

    string AchievementSnapshot()
    {
        // Read a bounded snapshot only; never call Poll/SetUserDir, which may award
        // or append a trophy. Catalog IDs and weights come from the linked source.
        try
        {
            string root = _profile.Str("user_dir");
            if (root.Length == 0)
                root = Path.Combine(_profile.Str("port_dir"), "user");
            root = Environment.ExpandEnvironmentVariables(root);
            string name = _profile.Str("player_profile", "Main");
            if (name.Length > 0 && !name.Equals("Main", StringComparison.OrdinalIgnoreCase))
                root = Path.Combine(root, "profiles", name);
            var path = Path.Combine(root, TrophyTracker.LogName);
            var ids = new HashSet<int>();
            if (File.Exists(path))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(file);
                for (int i = 0; i < 100000 && reader.ReadLine()is { } line; i++)
                {
                    var first = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (int.TryParse(first, out int id) && TrophyTracker.ById.ContainsKey(id))
                        ids.Add(id);
                }
            }

            int points = TrophyTracker.All.Sum(t => t.Points), earned = ids.Sum(id => TrophyTracker.ById[id].Points);
            return $"{ids.Count} / {TrophyTracker.All.Length} · {(points > 0 ? 100d * earned / points : 0):0}%";
        }
        catch
        {
            return "—";
        }
    }

    string GameVersion()
    {
        // Read only the existing dump metadata; no scan/decompression or game boot.
        try
        {
            string p = Path.Combine(_profile.Str("game_dir"), "sce_sys", "param.sfo");
            if (!File.Exists(p))
                return "Not found";
            var b = File.ReadAllBytes(p);
            int key = (int)BitConverter.ToUInt32(b, 8), data = (int)BitConverter.ToUInt32(b, 12), count = (int)BitConverter.ToUInt32(b, 16);
            string id = "", ver = "";
            for (int i = 0; i < count && 20 + i * 16 + 16 <= b.Length; i++)
            {
                int at = 20 + i * 16, k = key + BitConverter.ToUInt16(b, at), d = data + (int)BitConverter.ToUInt32(b, at + 12), len = (int)BitConverter.ToUInt32(b, at + 4);
                if (k < 0 || d < 0 || d + len > b.Length)
                    continue;
                int end = Array.IndexOf(b, (byte)0, k);
                if (end < 0)
                    continue;
                string name = System.Text.Encoding.UTF8.GetString(b, k, end - k), value = System.Text.Encoding.UTF8.GetString(b, d, len).TrimEnd('\0');
                if (name == "TITLE_ID")
                    id = value;
                if (name == "APP_VER")
                    ver = value;
            }

            return (id + " " + ver).Trim();
        }
        catch
        {
            return "—";
        }
    }

    void Graphics()
    {
        _surface = true;
        float left = _side - 8, bookTop = 38, bw = _width - left - 12, bh = _height - bookTop, edge = Math.Min(144, bw * .15f), ix = edge * .06f, iy = bh * .005f;
        Add(new(Ink.RoundImage, new Rect(left + ix, bookTop + iy, bw - ix * 2, bh - iy * 2), new Color4(1, 1, 1, .375f), Image: "menu-book-panel", Radius: Math.Min(edge * .24f, bh * .08f)));
        for (int side = 0; side < 2; side++)
        {
            float px = side == 0 ? left : left + bw - edge;
            Add(new(Ink.Image, new Rect(px, bookTop, edge, bh), new Color4(1, 1, 1, .25f), Image: "menu-book-leaf", Source: new Rect(0, 0, 240, 936), Flip: side == 1));
            float inner = bw / 2 - edge;
            for (int i = 0; i < 64; i++)
            {
                float t = (i + .5f) / 64, f = Math.Clamp((t - .35f) / .65f, 0, 1);
                f = f * f * (3 - 2 * f);
                float sx = 240 + 232f * i / 64, dx = side == 0 ? left + edge + inner * i / 64 : left + bw / 2 + inner * (63 - i) / 64;
                Add(new(Ink.Image, new Rect(dx, bookTop, inner / 64, bh), new Color4(1, 1, 1, .25f * (1 - f)), Image: "menu-book-leaf", Source: new Rect(sx, 0, 232f / 64, 936), Flip: side == 1));
            }
        }

        _surface = false;
        if (_page != 8)
            TextAt(_subpage.Length > 0 ? _subpage : PageCatalog.Titles[_page], new Rect(left + 40, 54, bw - 80, 32), ReferenceLayout.HeadingFont, true);
        float x = left + edge * .3f + 12, width = bw - edge * .6f - 36;
        _viewport = new Rect(x, 84, width, _height - 92);
        _clip = _viewport;
        float y = _viewport.Top;
        _scrollContent = true;
        if (_page == 1)
        {
            Button("Recommended settings", new Rect(x + width - 255 * _scale, y, 255 * _scale, 36 * _scale), "recommend", () => Run(OpenRecommendations));
            y += 46 * _scale;
            var gpu = new Option("", "Rendering GPU", "gpu_id", OptionKind.Choice, Choices: new[] { Choice.Literal(_profile.Ini.Get("gpu_name", UiText.T("Automatic")), _profile.Ini.Get("gpu_id", "-1")) }, Default: "-1") { Heading = true, Icon = "\uE950" };
            DrawOption(gpu, new Rect(x, y, width, 68 * _scale));
            y += 78 * _scale;
        }
        else if (_page is not (2 or 3 or 5 or 6 or 7 or 8))
            PageHeader(x, width, ref y);
        var options = _page == 1 ? GraphicsOptions : (_pageRows ??= PageCatalog.Build(_page, _profile));
        switch (_page)
        {
            case 2:
                DrawReferenceGame(x, width, ref y);
                break;
            case 3:
                DrawReferenceControls(x, width, ref y);
                if (_subpage.Length == 0)
                    DrawBindings(x, width, ref y);
                break;
            case 5:
                DrawReferenceOnline(x, width, ref y);
                break;
            case 6:
                DrawReferenceAchievements(x, width, ref y);
                break;
            case 7:
                DrawReferenceSettings(x, width, ref y);
                break;
            case 8:
                DrawReferenceMore(x, width, ref y);
                break;
            default:
                DrawReferenceSections(options.Where(o => GraphicsSchema.Visible(o, _profile)).ToArray(), x, width, ref y);
                break;
        }

        _scrollMax = Math.Max(0, y - _viewport.Bottom);
        _scroll = Math.Clamp(_scroll, 0, _scrollMax);
        _scrollContent = false;
        _clip = null;
        // The log already owns its vertical scrollbar; do not draw a second inert page bar.
        if (_page != 8 || _moreTab != 1 || _scrollMax > 0)
        {
            var bar = new Rect(_width - 28, _viewport.Top - 38, 8, _viewport.Height);
            Fill(bar, new Color4(.08f, .085f, .09f, .75f), 4);
            Add(new(Ink.ScrollThumb, bar, new Color4(.4f, .41f, .42f, 1)));
            HitAt(bar, "scroll", v => SetScroll(Math.Clamp(v, 0, 1) * _scrollMax));
        }
    }

    float OptionHeight(Option o, float width)
    {
        if (_page == 4 && o.Kind == OptionKind.Toggle)
            return Math.Max(52 * _scale, WrappedHeight(o.Title, 19.25f, width - 90 * _scale) + 26 * _scale);
        if (o.Key == "online_sign_timeout") return 106 * _scale;
        var label = _canvas.Measure(UiText.T(o.Title), ReferenceLayout.RowFont * _scale);
        return ReferenceLayout.PlaceRow(0, 0, width / _scale, label.X / _scale, o.Kind == OptionKind.Slider, o.Kind == OptionKind.Toggle, o.Key == "native-profile", label.Y / _scale).Height * _scale;
    }

    void DrawOption(Option o, Rect b)
    {
        int firstOp=_ops.Count, firstHit=_hits.Count;
        bool enabled = GraphicsSchema.Enabled(o, _profile);
        string value = _profile.Value(o);
        if (o.Key == "native-profile")
            value = _profile.Str("controls_profile." + _profile.InputDevice, o.Default);
        var measured = _canvas.Measure(UiText.T(o.Title), ReferenceLayout.RowFont * _scale);
        var row = ReferenceLayout.PlaceRow(b.X / _scale, b.Y / _scale, b.Width / _scale, measured.X / _scale, o.Kind == OptionKind.Slider, o.Kind == OptionKind.Toggle, o.Key == "native-profile", measured.Y / _scale);
        Rect D(Rect v) => new(v.X * _scale, v.Y * _scale, v.Width * _scale, v.Height * _scale);
        Rect label = D(row.Label), control = D(row.Control);
        if (o.Key == "online_sign_timeout")
        {
            label = new Rect(b.X + 10 * _scale, b.Y, b.Width - 20 * _scale, 32 * _scale);
            control = new Rect(b.X + 10 * _scale, b.Y + 34 * _scale, b.Width - 20 * _scale, 58 * _scale);
        }
        if (_page == 8 && o.Key.StartsWith("native-", StringComparison.Ordinal) && o.Kind == OptionKind.Toggle)
            label = new Rect(b.X + 10 * _scale, b.Y + 7 * _scale, Math.Max(40 * _scale, b.Width - (o.Key.StartsWith("native-mod:", StringComparison.Ordinal) ? 176 : 90) * _scale), b.Height - 22 * _scale);
        if (_page == 4 && o.Kind == OptionKind.Toggle)
            label = new Rect(b.X + 10 * _scale, b.Y + 7 * _scale, b.Width - 90 * _scale, b.Height - 22 * _scale);
        var colour = enabled ? Text : new Color4(Text.R, Text.G, Text.B, .4f);
        Line(b.X + 10 * _scale, b.Bottom - 12 * _scale, b.Width - 20 * _scale, new Color4(85 / 255f, 89 / 255f, 87 / 255f, .65f));
        if (o.Kind is OptionKind.Text or OptionKind.Password)
        {
            TextAt(o.Title, label, ReferenceLayout.RowFont, color: colour);
            Fill(control, new Color4(16 / 255f, 19 / 255f, 21 / 255f, 1));
            Line(control.X, control.Bottom - 1, control.Width, Muted);
            QueueNativeText(o.Key, control, value, o.Kind == OptionKind.Password, false, text => _profile.WriteOption(o, text));
            int textHit=HitAt(control,o.Key,_=>FocusNativeInput(o.Key),option:o);
            Add(new(Ink.Line,new Rect(control.X,control.Bottom-1,control.Width,1),Gold,Trigger:textHit,FocusOnly:true));
            return;
        }

        if (o.Kind != OptionKind.Action)
        {
            if ((_page == 4 || _page == 8 && o.Key.StartsWith("native-", StringComparison.Ordinal)) && o.Kind == OptionKind.Toggle)
                Wrapped(o.Title, label, ReferenceLayout.RowFont, colour);
            else if (o.Heading)
            {
                float iconWidth = o.Icon.Length > 0 ? 32 * _scale : 0;
                if (iconWidth > 0) Symbol(o.Icon, new Rect(label.X, label.Y, 26 * _scale, label.Height), Gold, 20);
                TextAt(o.Title, new Rect(label.X + iconWidth, label.Y, Math.Max(1, label.Width - iconWidth), label.Height),
                    ReferenceLayout.HeadingFont, color: enabled ? new Color4(215 / 255f, 216 / 255f, 213 / 255f, 1) : colour);
            }
            else
                TextAt(o.Title, label, ReferenceLayout.RowFont, color: colour);
        }

        int index = _sliderHitBase>=0?_sliderHitBase:_hits.Count;
        if (o.Kind == OptionKind.Action)
        {
            Button(o.Title, new Rect(b.X + 10 * _scale, b.Y + 7 * _scale, b.Width - 20 * _scale, 38 * _scale), o.Key, () => Run(() => PageAction(o.Key[7..])), enabled: enabled);
            return;
        }

        if (o.Kind == OptionKind.Toggle)
        {
            bool on = _profile.IsOn(o);
            Fill(new Rect(control.X + 4 * _scale, control.Y + 5 * _scale, 50 * _scale, 24 * _scale), new Color4(20 / 255f, 22 / 255f, 23 / 255f, enabled ? 1 : .4f), 12 * _scale);
            float knobX = on ? control.Right - 25 * _scale : control.X + 7 * _scale;
            Fill(new Rect(knobX, control.Y + 8 * _scale, 18 * _scale, 18 * _scale), new Color4(80 / 255f, 83 / 255f, 84 / 255f, enabled ? 1 : .4f), 9 * _scale);
            if (on && enabled)
            {
                var halo = new Rect(control.Right - 43 * _scale, control.Y - 3 * _scale, 54 * _scale, 40 * _scale);
                Add(new(Ink.Glow, halo, new Color4(1, 1, 1, 1)));
                Add(new(Ink.Glow, halo, new Color4(1, 1, 1, 1)));
            }
        }
        else if (o.Kind == OptionKind.Slider)
        {
            double n = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.Parse(o.Default, CultureInfo.InvariantCulture);
            string suffix = o.Key == "stick_turn_rate" ? "°/s" : o.Key == "pad_deadzone" ? "%" : "";
            string format = o.Step >= 1 ? "0" : "0.00";
            string number = n.ToString(format, CultureInfo.InvariantCulture) + suffix;
            // Measure in the same monospace font used for drawing, reserving the
            // entire range so the track does not jump as digit counts change.
            float widest = Math.Max(_canvas.Measure(number, 18.375f * _scale, mono: true).X,
                Math.Max(_canvas.Measure(o.Min.ToString(format, CultureInfo.InvariantCulture) + suffix, 18.375f * _scale, mono: true).X,
                         _canvas.Measure(o.Max.ToString(format, CultureInfo.InvariantCulture) + suffix, 18.375f * _scale, mono: true).X));
            float valueWidth = Math.Max(56 * _scale, widest + 12 * _scale);
            Add(new(Ink.Text, new Rect(control.X, control.Y, valueWidth, 32 * _scale), colour, number, 18.375f * _scale, Center: true, Mono: true));
            float left = control.X + valueWidth + 19 * _scale, right = control.Right - 9 * _scale;
            float fraction = (float)Math.Clamp((n - o.Min) / (o.Max - o.Min), 0, 1);
            var track = new Rect(left + 12 * _scale, control.Y + 15 * _scale, Math.Max(1, right - left - 24 * _scale), 2 * _scale);
            Fill(track, new Color4(115 / 255f, 120 / 255f, 117 / 255f, enabled ? 1 : .4f));
            float at = track.X + track.Width * fraction;
            var grip = new Rect(at - 10.5f * _scale, track.Y, 21 * _scale, 2 * _scale);
            Fill(grip, new Color4(115 / 255f, 120 / 255f, 117 / 255f, enabled ? 1 : .4f));
            Add(new(Ink.Fill, grip, new Color4(179 / 255f, 160 / 255f, 114 / 255f, 1), Trigger: index));
            Fill(new Rect(at - 3.5f * _scale, track.Y - 2.5f * _scale, 7 * _scale, 7 * _scale), enabled ? Gold : Muted, 3.5f * _scale);
            control = new Rect(track.X, control.Y, track.Width, 32 * _scale);
        }
        else
        {
            ControlFrame(control, index, enabled: enabled);
            string text = o.Kind == OptionKind.Password ? (value.Length > 0 ? "••••••••" : "—") : o.Choices?.FirstOrDefault(c => c.Value == value)?.Label ?? value;
            if (o.Key == "online_sign_timeout")
                Add(new(Ink.Text, new Rect(control.X + 12 * _scale, control.Y, Math.Max(1, control.Width - 38 * _scale), control.Height), colour, text, 17 * _scale, Center: true, Wrap: true));
            else TextAt(text, new Rect(control.X + 26 * _scale, control.Y, Math.Max(1, control.Width - 52 * _scale), control.Height), 18.375f, o.Key != "display_device", colour, detail: o.Kind is OptionKind.Text or OptionKind.Folder, translate: false);
            if (o.Kind == OptionKind.Choice)
                TextAt("⌄", new Rect(control.Right - 25 * _scale, control.Y, 20 * _scale, control.Height), 18.375f, true, Gold);
        }

        HitAt(control, o.Key, position => Change(o, position, ScreenRect(control, true)), o.Hint ?? (_profile.Help.TryGetValue(o.Key, out var help) ? help : o.Choices?.FirstOrDefault(c => c.Value == value)?.Note ?? ""), o, enabled);
        if(o.Kind==OptionKind.Slider && _sliderHitBase<0)_sliderRows[o.Key]=new(b,firstOp,_ops.Count-firstOp,firstHit);
    }

    void Change(Option o, float position, Rect anchor)
    {
        ClearTooltip();
        if (o.Key is "native-trophy-filter" or "native-account-method" or "native-save-source")
        {
            _popupOffset = 0;
            _popup = new(o, anchor);
            Rebuild();
            return;
        }

        if (o.Key == "native-output-follow")
        {
            _followOutput = !_followOutput;
            if (_followOutput)
            {
                _outputOffset = _outputMax;
                _outputFollowPending = true;
            }

            Rebuild();
            return;
        }

        if (!GraphicsSchema.Enabled(o, _profile))
            return;
        if (o.Kind == OptionKind.Action)
        {
            Run(() => PageAction(o.Key[7..]));
            return;
        }

        if (o.Kind == OptionKind.Folder)
        {
            string? folder = NativeDialogs.Folder(_window, UiText.T(o.Title));
            if (folder != null)
                _profile.Set(o.Key, folder, o.Setting);
            _pageRows = null;
            Rebuild();
            return;
        }

        if (o.Kind is OptionKind.Text or OptionKind.Password)
        {
            Edit(o.Title, _profile.Value(o), text =>
            {
                if (o.Kind == OptionKind.Password)
                    _profile.EditSource((s, ini) => s.OnlinePassword = text);
                else
                    _profile.WriteOption(o, text);
            }, o.Kind == OptionKind.Password);
            return;
        }

        if (o.Key == PreferredGamepads.Key)
        {
            RefreshPreferredPads();
            o = PreferredGamepads.Option(_preferredPads ?? Array.Empty<SdlPad.Device>(), _profile.Str(PreferredGamepads.Key));
        }

        if (o.Key.StartsWith("native-mod:", StringComparison.Ordinal) && _modEntries != null)
        {
            int i = int.Parse(o.Key[11..]);
            _modEntries[i] = _modEntries[i] with
            {
                Enabled = !_modEntries[i].Enabled
            };
            _profile.WithSidecars(() =>
            {
                Mods.Save(_modEntries);
                return true;
            }, "mods.json");
            RefreshReferenceMods();
            Rebuild();
            return;
        }

        if (o.Key.StartsWith("native-patch:", StringComparison.Ordinal) && _patchEntries != null)
        {
            int i = int.Parse(o.Key[13..]);
            var profile = _profile.WithSidecars(Patches.LoadProfile);
            _profile.WithSidecars(() =>
            {
                Patches.Save(_patchEntries.Select((p, n) => (p, n == i ? !Patches.IsOn(p, profile) : Patches.IsOn(p, profile))));
                return true;
            }, "patches.json");
            RefreshReferenceMods();
            Rebuild();
            return;
        }

        if (o.Kind == OptionKind.Toggle)
        {
            bool on = _profile.IsOn(o);
            string next = o.Setting ? (!on).ToString().ToLowerInvariant() : on ? "0" : "1";
            if (o.Key.StartsWith("env:", StringComparison.Ordinal))
            {
                var item = EngineSwitches.All.First(i => i.Key == o.Key);
                next = _profile.Value(o) == item.On ? item.Off : item.On;
            }

            _profile.WriteOption(o, next);
        }
        else if (o.Kind == OptionKind.Slider)
        {
            double v = Math.Clamp(o.Min + Math.Clamp(position, 0, 1) * (o.Max - o.Min), o.Min, o.Max);
            v = Math.Clamp(Math.Round(v / o.Step) * o.Step, o.Min, o.Max);
            string next = v.ToString("0.###", CultureInfo.InvariantCulture);
            if (_profile.Value(o) == next) return;
            _profile.WriteOption(o, next);
            if(RefreshSliderRow(o))return;
        }
        else
        {
            if (o.Key == "gpu_id")
                o = o with
                {
                    Choices = new[]
                    {
                        Choice.Literal(UiText.T("Automatic"), "-1")
                    }.Concat(GpuAdapters.All().Select(g => Choice.Literal(g.Label, g.Index.ToString()))).ToArray()
                };
            _popupOffset = 0;
            _popup = new(o, anchor);
        }

        Rebuild();
    }

    void DrawPopup()
    {
        var p = _popup!;
        var list = p.Option.Choices ?? Array.Empty<Choice>();
        bool monitors = p.Option.Key == "display_device" || p.Option.Key == PreferredGamepads.Key;
        float width = Math.Min(Math.Max(p.Anchor.Width, 260 * _scale), _width - 20);
        if (monitors && list.Count > 0)
            width = Math.Min(_width - 24, Math.Max(width, list.Max(c => _canvas.Measure(c.Label, 18.375f * _scale).X) + 44 * _scale));
        float RowHeight(Choice c) => monitors ? Math.Max(34 * _scale, WrappedHeight(c.Label, 18.375f, width - 28 * _scale, false) + 12 * _scale) : 34 * _scale;
        float h = Math.Min(_height - 50, list.Skip(_popupOffset).Sum(RowHeight) + 8);
        float y = Math.Clamp(p.Anchor.Bottom + 4, 34, _height - h - 6), x = Math.Clamp(p.Anchor.X, 8, _width - width - 8);
        Fill(new Rect(x, y, width, h), new Color4(16 / 255f, 18 / 255f, 19 / 255f, 1));
        float at = y + 4;
        for (int i = 0; i + _popupOffset < list.Count; i++)
        {
            var c = list[i + _popupOffset];
            float row = RowHeight(c);
            if (at + row > y + h)
                break;
            var b = new Rect(x + 4, at, width - 8, row);
            at += row;
            int n = _hits.Count;
            Add(new(Ink.ControlSurface, b, new Color4(1, 1, 1, 1), Trigger: n));
            _glows.Add(Add(new(Ink.Glow, b, new Color4(1, 1, 1, .8f), Trigger: n)));
            if (monitors)
                Wrapped(c.Label, new Rect(b.X + 12 * _scale, b.Y, b.Width - 24 * _scale, b.Height), 18.375f, c.Value == _profile.Value(p.Option) ? Gold : Text, false);
            else
                TextAt(c.Label, b, 18.375f, true, c.Value == _profile.Value(p.Option) ? Gold : Text, translate: false);
            HitAt(b, "choice" + i, _ =>
            {
                if (c.Value == "custom" && p.Option.Key == "output_res")
                {
                    _editing = p.Option;
                    _editText = _profile.Ini.Get("output_res", "1920x1080");
                }
                else
                {
                    if (p.Option.Key == "native-trophy-filter")
                        _trophyFilter = c.Value;
                    else if (p.Option.Key == "native-account-method")
                    {
                        if (c.Value == "discord")
                            LinkAccount();
                        else
                            PageAction(c.Value == "local" ? "online-create" : "online-recover");
                    }
                    else if (p.Option.Key == "native-save-source")
                    {
                        if (_importSources != null && Confirm(UiText.T("Import saves")))
                            SaveImport.Import(_profile.SourceSettings(), _importSources[int.Parse(c.Value)].Dir);
                        _importSources = null;
                    }
                    else if (p.Option.Key.StartsWith("pad:", StringComparison.Ordinal))
                        _profile.EditSource((s, ini) => PadMap.Assign(ini, p.Option.Key[4..], c.Value));
                    else if (p.Option.Key == "restore-backup")
                    {
                        if (_restoreBackups != null && Confirm(UiText.T("Restore backup")))
                            SaveBackups.Restore(_profile.SourceSettings(), _restoreBackups[int.Parse(c.Value)]);
                    }
                    else if (p.Option.Key == "native-profile")
                        _profile.UseProfile(_profile.Profiles().First(p => p.Name == c.Value));
                    else if (p.Option.Key == "input_mode")
                        _profile.Input(c.Value);
                    else
                        _profile.WriteOption(p.Option, c.Value);
                    if (p.Option.Key == "text_language")
                    {
                        _profile.Set("menu_language", GameLanguages.MenuLocale(c.Value));
                        UiText.UseTextLanguage(c.Value);
                    }

                    _pageRows = null;
                    if (p.Option.Key == "gpu_id")
                        _profile.Set("gpu_name", GpuAdapters.All().FirstOrDefault(g => g.Index.ToString() == c.Value)?.Name ?? "");
                }

                _popup = null;
                Rebuild();
            });
        }
    }

    void Mouse(float x, float y, bool down)
    {
        if(down || x!=_mouseX || y!=_mouseY)EndPadCue();
        _mouseX = x;
        _mouseY = y;
        if(down && _page==0 && (_globalSearchActive || GlobalSearchFocused))
        {
            int selected=FindHit(x,y);
            if(selected<0 || (_hits[selected].Id!=GlobalSearchKey && !_hits[selected].Id.StartsWith("settings-result-",StringComparison.Ordinal)))
            {
                _globalSearchActive=false;
                _focusGlobalAfterSync=false;
                // Native child editors keep keyboard focus until it is moved
                // explicitly, even when clicking an empty parent-window area.
                if(_textEditors.TryGetValue(GlobalSearchKey,out var search) && GetFocus()==search.Window)
                    W.SetFocus(_window);
                if(_focus>=0 && _focus<_hits.Count && _hits[_focus].Id==GlobalSearchKey)SetFocus(-1);
                Rebuild();
            }
        }
        if (_dialogClosing) return;
        if (_messagePrompt != null) { MessageMouse(x, y, down); return; }
        if (_resetPrompt != null)
        {
            int modal = FindHit(x, y);
            bool button = modal >= 0 && _hits[modal].Id is "controls-reset-accept" or "controls-reset-cancel";
            bool information = modal >= 0 && _hits[modal].Id == "controls-reset-info";
            UpdateHover(button || information ? modal : -1);
            if (down && button)
            {
                _audio.Play(MenuSoundCue.Change);
                Run(() => _hits[modal].Action?.Invoke(0));
            }
            return;
        }
        if (_recommendation != null)
        {
            int modal = FindHit(x, y);
            bool button = modal >= 0 && _hits[modal].Id.StartsWith("recommend-", StringComparison.Ordinal);
            UpdateHover(button ? modal : -1);
            if (down && button)
            {
                _audio.Play(MenuSoundCue.Change);
                _hits[modal].Action?.Invoke(0);
            }

            return;
        }

        if (_binding != null)
        {
            if (down && !ActivateModal(x, y))
                CaptureAtom("Mouse1", true);
            return;
        }

        if (_textCommit != null || _padCapture != null)
        {
            if (down)
                ActivateModal(x, y);
            return;
        }

        if (_editing != null)
            return;
        if (_drag != null && !down)
        {
            if (_drag.Id == "scroll")
            {
                SetScroll(_scrollDragStart + (y - _scrollDragY) * 3);
                SyncNativeText();
                return;
            }

            var dragBounds = _drag.Id.StartsWith("output-scroll", StringComparison.Ordinal) ? ScreenRect(_drag.Bounds, _drag.Scrollable) : _drag.Bounds;
            float fraction = _drag.Id is "scroll" or "output-scroll" ? (y - dragBounds.Y) / dragBounds.Height : (x - dragBounds.X) / Math.Max(1, dragBounds.Width);
            if (_drag.Option?.Kind == OptionKind.Slider) _sliderPending = fraction;
            else _drag.Action?.Invoke(fraction);
            return;
        }

        int hovered = FindHit(x, y);
        if (_popup != null && (hovered < 0 || !_hits[hovered].Id.StartsWith("choice")))
        {
            if (down)
            {
                _popup = null;
                Rebuild();
            }

            return;
        }

        UpdateHover(hovered);
        if (!down)
            return;
        if (hovered < 0)
        {
            if (_popup != null)
            {
                _popup = null;
                Rebuild();
            }

            return;
        }

        var hit = _hits[hovered];
        if (!hit.Enabled)
            return;
        if (hit.Id != "binding-search")
            _searchActive = false;
        SetFocus(hovered);
        if (hit.Id is not ("min" or "max" or "close" or "mute" or "output-text" or "output-scroll" or "output-scroll-x" or "scroll"))
            _audio.Play(hit.Id == "play" ? MenuSoundCue.Start : hit.Id.StartsWith("nav") ? MenuSoundCue.Select : MenuSoundCue.Change);
        if (hit.Id == "scroll")
        {
            var thumb = ScrollGeometry.Thumb(hit.Bounds, _scrollMax, _scroll);
            if (!thumb.Contains(x, y))
                SetScroll(_scroll + Math.Sign(y - thumb.Center.Y) * 240 * _scale);
            _scrollDragStart = _scroll;
            _scrollDragY = y;
        }

        if (hit.Id is "scroll" or "output-scroll" or "output-scroll-x" || hit.Option?.Kind == OptionKind.Slider)
        {
            _drag = hit;
            W.SetCapture(_window);
        }

        var hitBounds = hit.Id.StartsWith("output-scroll", StringComparison.Ordinal) ? ScreenRect(hit.Bounds, hit.Scrollable) : hit.Bounds;
        float position = hit.Id is "scroll" or "output-scroll" ? (y - hitBounds.Y) / hitBounds.Height : (x - hitBounds.X) / Math.Max(1, hitBounds.Width);
        if (hit.Id != "scroll")
            Run(() => hit.Action?.Invoke(position));
    }

    void Key(int key)
    {
        if (_dialogClosing) return;
        if (_messagePrompt != null) { MessageKey(key); return; }
        if (_resetPrompt != null)
        {
            if (key is 0x1B or 0x0D) { _audio.Play(MenuSoundCue.Change); Run(() => CloseControlReset(key == 0x0D)); }
            return;
        }
        if (_recommendation != null)
        {
            if (key == 0x1B)
            {
                CloseRecommendations();
            }
            else if (key == 0x0D)
                ApplyRecommendations();
            else if (key is 0x26 or 0x28 or 0x21 or 0x22)
                ScrollRecommendations((key is 0x26 or 0x21 ? -1 : 1) * (key is 0x21 or 0x22 ? 180 : 48) * _scale);
            return;
        }

        if (_padCapture != null)
        {
            if (key == 0x1B)
                EndReferenceCapture();
            return;
        }

        if (_page==0 && _globalSearchActive && _globalResults.Count>0 && key is 0x26 or 0x28)
        { MoveGlobalResult(key==0x28?1:-1); return; }
        if (_searchActive)
        {
            if (key is 0x1B or 0x0D)
            {
                _searchActive = false;
                Rebuild();
            }

            return;
        }

        if (EditKey(key))
            return;
        if (_editing != null)
        {
            if (key == 0x1B)
            {
                _editing = null;
                Rebuild();
            }
            else if (key == 0x0D)
            {
                var parts = _editText.ToLowerInvariant().Replace(" ", "").Split('x', '×');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && Choices.ValidCustomSize(w, h))
                {
                    _profile.Set(_editing.Key, $"{w & ~1}x{h & ~1}");
                    _editing = null;
                    Rebuild();
                }
                else
                {
                    _notice = "Valid size: 640 x 360 to 16384 x 8640.";
                    Rebuild();
                }
            }

            return;
        }

        if (_popup != null)
        {
            if (key == 0x1B)
                _popup = null;
            else if (key is 0x26 or 0x28)
                _popupOffset = Math.Clamp(_popupOffset + (key == 0x28 ? 1 : -1), 0, Math.Max(0, (_popup.Option.Choices?.Count ?? 0) - 1));
            else if (key == 0x0D)
                _hits.FirstOrDefault(h => h.Id == "choice0")?.Action?.Invoke(0);
            Rebuild();
            return;
        }

        if (key == 0x1B && _page == 0 && _globalSearchActive) { GlobalSearchKeyDown(key); return; }
        if (key == 0x1B)
        {
            _popup = null;
            if (_page != 0)
                Switch(0);
            Rebuild();
            return;
        }

        if (key == 0x09)
        {
            SetFocus((_focus + 1) % Math.Max(1, _hits.Count));
            return;
        }

        if (key == 0x0D && _focus >= 0 && _focus < _hits.Count)
        {
            Run(() => _hits[_focus].Action?.Invoke(.5f));
            return;
        }

        if (key is 0x21 or 0x22)
        {
            SetScroll(_scroll + (key == 0x22 ? 1 : -1) * _viewport.Height * .8f);
            return;
        }

        if (_focus >= 0 && _focus < _hits.Count && _hits[_focus].Option is Option o && o.Kind == OptionKind.Slider && key is 0x25 or 0x27)
        {
            double.TryParse(_profile.Value(o), NumberStyles.Float, CultureInfo.InvariantCulture, out double n);
            string next=Math.Clamp(n+(key==0x27?o.Step:-o.Step),o.Min,o.Max).ToString(CultureInfo.InvariantCulture);
            if(_profile.Value(o)==next)return;
            _profile.WriteOption(o,next);
            if(!RefreshSliderRow(o))Rebuild();
        }
    }

    void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _notice = e.GetBaseException().Message;
            Rebuild();
        }
    }

    void SaveReport()
    {
        if (!_diagnostics) return;
        var report = new
        {
            backend = _canvas.Backend,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            assets = _profile.Assets,
            launcher = "native-default",
            sourceIntegration = "source-linked Choices/IniFile/UiText/GpuAdapters/WindowsMonitors/UpscalerLayout",
            note = "Game launch uses source-linked GameRunner and GameEnvironment; no legacy launcher process.",
            samples = _samples
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_report))!);
        File.WriteAllText(_report, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        _navigationMemory.Dispose();
        StopLauncherPad();
        if (_game != null)
        {
            _game.EventsAvailable -= WakeGameEvents;
            _game.Dispose();
        }
        DisposeNativeText();
        DisposeTrophyQueue();
        _cancel.Cancel();
        _cancel.Dispose();
        if (!_closed && _window != 0)
            W.DestroyWindow(_window);
        _profile.SavePreview();
        SdlPad.Stop();
        _audio.Dispose();
        _canvas.Dispose();
    }
}
