using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BloodborneLauncher.Core;
using BloodborneLauncher.Controls;
using Button = BloodborneLauncher.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = BloodborneLauncher.Controls.TextBox;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// The engine's PC controls: mouse camera and menu pointer, profiles, and the 30 actions, each
    /// stored in bbport.ini bind.*, the same records the in-game Key Bindings menu edits.
    /// Capture accumulates keys and mouse inputs until they are released. A single key
    /// or mouse input keeps the alternative of the other kind. The capture dialog clears a binding;
    /// right-click unbinds the action. Inputs used twice, and the port's Insert key, are flagged.
    /// </summary>
    public sealed class ControlsPage : LauncherPage
    {
        private readonly StackPanel _table = new OptionRows();
        private readonly StackPanel _movementOptions = new OptionRows();
        private readonly StackPanel _gamepadOptions = new OptionRows();
        private readonly StackPanel _mouseSection = new OptionRows();
        private readonly Segmented _inputSelector;
        private string? _tableStamp, _profileFilesStamp, _profilesViewStamp;
        private readonly DispatcherTimer _chordTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
        private readonly BindingCapture _chordCapture = new();
        private UIElement? _captureInputHost;
        private Window? _captureWindow;
        private static readonly Key[] CaptureKeys = Enum.GetValues<Key>().Distinct()
            .Where(k => k != Key.None && InputMap.SdlKeyName(k) != null && KeyInterop.VirtualKeyFromKey(k) != 0).ToArray();

        private readonly StackPanel _mouseOptions;
        private readonly ToggleSwitch _pcToggle, _mouseCameraToggle, _mouseMenuToggle;
        private readonly InfoBar _conflicts;
        private readonly TextBox _search;
        private readonly TextBlock _padStatus;

        // Capture overlay
        private readonly Grid _overlay;
        private readonly TextBlock _captureTitle, _captureFeedback, _captureHelp, _captureBoxText;
        private readonly WrapPanel _padChoices;
        private readonly DispatcherTimer _padTimer;
        private string? _padInput;
        private ControlDef? _padControl;
        private HashSet<string> _padHeld = new();
        private ulong? _padState;
        private DispatcherTimer? _statusPoll,_activityPoll;
        private DateTime? _padSeen;
        private ControlDef? _target;
        private bool _addMode;
        private readonly Button _addBindingButton;
        public bool IsCapturingInput => _target != null || _padInput != null;

        // Profiles
        private readonly ComboBox _profileBox;
        private readonly TextBlock _profileHint;
        private readonly Button _deleteProfile;
        private List<ControlProfiles.Profile> _profiles = new();
        private bool _fillingProfiles;

        public ControlsPage() : base(BloodborneLauncher.Core.UiText.T("Controls"),
            BloodborneLauncher.Core.UiText.T("Keyboard and mouse for every action, the mouse camera, and shareable profiles. Gamepads work as on PS4."))
        {
            // Upgrade the previous stock movement preset; retain other custom combinations.
            if(Ini.Get("movement_defaults_version","1")!="2") {
                if(Ini.Raw("stick_turn_rate")=="900" && (Ini.Get("stick_circular","0") is "0" or "off")) {
                    Ini.Set("stick_turn_rate","1200");Ini.Set("stick_circular","1");
                }
                Ini.Set("movement_defaults_version","2");Ws.SaveSoon();
            }
            if(Ini.Raw("input_mode")==null){
                Ini.Set("input_mode","keyboard_mouse");
                if(Ini.Raw("bind.attack")==null)ControlProfiles.Apply(S,Ini,ControlProfiles.BuiltIns.First(p=>p.Name==ControlProfiles.DarkSouls3Name));
            }
            var inputChoices = new[]{new Choice(PcText.Get("Keyboard/Mouse"),"keyboard_mouse"),new Choice(UiText.T("Gamepad"),"controller")};
            _inputSelector = new Segmented(inputChoices,spacing:14,uniform:true,fontSize:22,minHeight:50);
            _inputSelector.Select(Choices.IndexOf(inputChoices,Ini.Get("input_mode","keyboard_mouse")),notify:false);
            _inputSelector.Changed += index => UseInputMode(inputChoices[index].Value=="keyboard_mouse");
            System.Windows.Automation.AutomationProperties.SetName(_inputSelector,PcText.Get("Input device"));
            var inputCard=Card(SymbolRegular.Keyboard24,"",null,null,out _);
            inputCard.Child=_inputSelector;
            Add(inputCard); SettingsColumns.SetFullWidth(inputCard, true);
            // ==================== Profiles ====================
            _profileBox = new ComboBox { Width = 260, VerticalAlignment = VerticalAlignment.Center };
            _profileBox.SelectionChanged += (_, _) => ProfileChosen();
            _deleteProfile = IconButton(SymbolRegular.Delete24, BloodborneLauncher.Core.UiText.T("Delete this profile"), (_, _) => DeleteProfile());
            var profileCard = Add(Card(SymbolRegular.PeopleCommunity24, UiText.T("Profile"), "",
                Row(_profileBox, _deleteProfile), out _profileHint));
            profileCard.TightLabel = true;
            var share = new WrapPanel { Margin = new Thickness(10, 0, 0, 12) };
            share.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Save as…").TrimEnd('.', '…'), SymbolRegular.Save24, (_, _) => SaveProfile()));
            share.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Copy JSON"), SymbolRegular.Copy24, (_, _) => CopyProfile()));
            share.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Export file…").TrimEnd('.', '…'), SymbolRegular.ArrowExportUp24, (_, _) => ExportProfile()));
            share.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Import file…").TrimEnd('.', '…'), SymbolRegular.DocumentArrowDown20, (_, _) => ImportFile()));
            share.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Import from link…").TrimEnd('.', '…'), SymbolRegular.Link24, async (_, _) => await ImportLinkAsync()));
            foreach (FrameworkElement b in share.Children) b.Margin = new Thickness(0, 4, 6, 0);
            Body.Children.Add(share);


            SliderRow(SymbolRegular.Gauge24,BloodborneLauncher.Core.UiText.T(PcText.Get("Turning speed")),BloodborneLauncher.Core.UiText.T(PcText.Get("Smooths direction changes to help prevent sprint interruptions when combining movement keys. Default: 1200 degrees/s. Zero turns instantly.")),
                0,5000,50,()=>ParseDouble(Ini.Get("stick_turn_rate","1200"),1200),v=>Ini.Set("stick_turn_rate",((int)v).ToString()),"0'°/s'",_movementOptions);
            SliderRow(SymbolRegular.Timer24,BloodborneLauncher.Core.UiText.T(PcText.Get("Key release delay")),BloodborneLauncher.Core.UiText.T(PcText.Get("Keep the last direction briefly while sprint is held. Default: 0 ms. Zero stops immediately.")),
                0,200,5,()=>ParseDouble(Ini.Get("stick_release_delay_ms","0"),0),v=>Ini.Set("stick_release_delay_ms",((int)v).ToString()),"0' ms'",_movementOptions);
            ToggleRow(SymbolRegular.ArrowSync24,BloodborneLauncher.Core.UiText.T(PcText.Get("Circular stick range")),BloodborneLauncher.Core.UiText.T(PcText.Get("Use a circular range for keyboard sprint movement. Off keeps square diagonals.")),
                ()=>Ini.Get("stick_circular","1") is "1" or "on",v=>Ini.Set("stick_circular",v?"1":"0"),_movementOptions);
            Group("", _mouseSection).Visibility = Visibility.Collapsed;
            var pc=new ToggleSwitch{IsChecked=true};
            _pcToggle=pc;
            var mouse = new OptionRows();
            _mouseOptions = mouse;
            _mouseCameraToggle = ToggleRow(SymbolRegular.CursorClick24, BloodborneLauncher.Core.UiText.T("Mouse camera"), BloodborneLauncher.Core.UiText.T("The mouse turns the game's own camera by raw counts, at any frame rate"),
                () => Ini.Get("mouse_camera", "1") != "0", v => Ini.Set("mouse_camera", v ? "1" : "0"), mouse);
            _mouseMenuToggle = ToggleRow(SymbolRegular.CursorHover24, BloodborneLauncher.Core.UiText.T("Mouse in menus"), BloodborneLauncher.Core.UiText.T("Hover and click items; move with WASD while browsing the inventory. WASD still navigates the title menus"),
                () => Ini.Get("mouse_menu", "1") != "0", v => Ini.Set("mouse_menu", v ? "1" : "0"), mouse);
            SliderRow(SymbolRegular.Gauge24, BloodborneLauncher.Core.UiText.T("Mouse sensitivity"), BloodborneLauncher.Core.UiText.T("5 is the default; below 0 for high-DPI mice (each step 20% slower)"), -10, 20, 1,
                () => ParseDouble(Ini.Get("mouse_sensitivity", "5"), 5), v => Ini.Set("mouse_sensitivity", ((int)v).ToString()), "0", mouse);
            IniToggle(SymbolRegular.ArrowSwap24, BloodborneLauncher.Core.UiText.T("Invert horizontal"), null, "mouse_invert_x", mouse);
            IniToggle(SymbolRegular.ArrowSortDown24, BloodborneLauncher.Core.UiText.T("Invert vertical"), null, "mouse_invert_y", mouse);
            IniToggle(SymbolRegular.ArrowRotateClockwise24, BloodborneLauncher.Core.UiText.T("Camera auto-rotation"), BloodborneLauncher.Core.UiText.T("Let the camera turn by itself with movement, as with a gamepad"),
                "mouse_auto_rotation", mouse);
            ComboRow(SymbolRegular.KeyboardShift24, BloodborneLauncher.Core.UiText.T("Button prompts"),
                BloodborneLauncher.Core.UiText.T("The button icons in the game's menus and prompts. Automatic shows your keys while the keyboard or mouse is in use, and the controller's buttons with a controller; also in-game under System › Controls"),
                Choices.ButtonPrompts, () => Ini.Get("button_prompts", "auto"), v => Ini.Set("button_prompts", v), mouse);
            mouse.Children.Add(_movementOptions);
            _mouseSection.Children.Add(mouse);
            Body.Children.Add(_mouseSection);
            ToggleRow(SymbolRegular.CursorHoverOff24, BloodborneLauncher.Core.UiText.T("Hide mouse cursor"), BloodborneLauncher.Core.UiText.T("Hidden 2 seconds after the mouse stops moving; moving it brings it back"),
                () => Ini.Get("hide_cursor", "1") != "0", v => Ini.Set("hide_cursor", v ? "1" : "0"), _mouseSection);
            void Enable() => mouse.IsEnabled = pc.IsChecked == true;
            pc.Click += (_, _) => { Enable(); Rebuild(); };
            Enable();

            var gamepadHeading = Group("", _gamepadOptions);
            gamepadHeading.Visibility = Visibility.Collapsed;
            SettingsColumns.SetTwoColumns(gamepadHeading, true);
            Add(Card(SymbolRegular.XboxController24, BloodborneLauncher.Core.UiText.T("Gamepad"), "", null, out _padStatus), _gamepadOptions);
            ComboRow(SymbolRegular.Games24, BloodborneLauncher.Core.UiText.T("Controller icons"),
                BloodborneLauncher.Core.UiText.Format("The controller\u0027s buttons in the game\u0027s menus and prompts. Automatic shows PlayStation buttons with a PlayStation controller and Xbox buttons (A, B, X, Y, LB, RT\u2026) with any other; rebound buttons show as bound. Also in-game under System \u203A Controls"),
                Choices.PadIcons, () => Ini.Get("pad_icons", "auto"), v => Ini.Set("pad_icons", v), _gamepadOptions);
            IniToggle(SymbolRegular.ArrowSwap24, BloodborneLauncher.Core.UiText.T("Swap sticks"), BloodborneLauncher.Core.UiText.T("Move with the right stick and look with the left"), "pad_swap_sticks", _gamepadOptions, changed: () => Rebuild());
            IniToggle(SymbolRegular.ArrowBidirectionalLeftRight24, BloodborneLauncher.Core.UiText.T("Invert camera left / right"), BloodborneLauncher.Core.UiText.T("The camera stick's horizontal direction"), "pad_invert_look_x", _gamepadOptions);
            IniToggle(SymbolRegular.ArrowBidirectionalUpDown24, BloodborneLauncher.Core.UiText.T("Invert camera up / down"), BloodborneLauncher.Core.UiText.T("The camera stick's vertical direction"), "pad_invert_look_y", _gamepadOptions);
            SliderRow(SymbolRegular.CircleSmall24, BloodborneLauncher.Core.UiText.T("Stick dead zone"),
                BloodborneLauncher.Core.UiText.T("Ignores small stick movement, for sticks that drift. 0 = off (the game's own)"), 0, 30, 1,
                () => double.TryParse(Ini.Get("pad_deadzone", "0"), out var v) ? v : 0,
                v => Ini.Set("pad_deadzone", ((int)Math.Round(v)).ToString()), format: "0'%'", into: _gamepadOptions);

            Body.Children.Add(_gamepadOptions);

            // ==================== Bindings ====================
            var header = new Grid { Margin = new Thickness(2, 24, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            var title = Text(BloodborneLauncher.Core.UiText.T("Keybindings"), "GroupHeader");
            title.Margin = new Thickness(0);
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(new MenuSurface { Child = title, Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left });
            _search = new TextBox { PlaceholderText = BloodborneLauncher.Core.UiText.T("Filter actions or keys"), PlaceholderEnabled = true, Width = 240, Height = 42, FontSize = 16, VerticalContentAlignment = VerticalAlignment.Center, Icon = Icon(SymbolRegular.Search24) };
            _search.TextChanged += (_, _) => Rebuild();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var resetAll = SmallButton(BloodborneLauncher.Core.UiText.T("Reset all"), SymbolRegular.ArrowUndo24, (_, _) =>
            {
                Bindings.ResetAll(Ini);
                PadMap.ResetAll(Ini);
                Ws.Log(BloodborneLauncher.Core.UiText.T("Key and controller bindings reset to the defaults."), LogLevel.Step);
                Changed();
            });
            _search.Margin = new Thickness(14, 0, 10, 0);
            Grid.SetColumn(_search, 1); Grid.SetColumn(resetAll, 3);
            resetAll.FontSize = 16;
            resetAll.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(_search); header.Children.Add(resetAll);
            MenuSurface.SetKeybindingsShadow(header,true);
            header.Tag = "group"; SettingsColumns.SetFullWidth(header, true);
            Body.Children.Add(header);

            var help = Text(BloodborneLauncher.Core.UiText.Format("Click an action, hold the keys or mouse buttons together and release all to finish. Use Unbind in the capture dialog to clear the action. In the Controller column, press the button to assign it; buttons already in use swap actions."), "Faint");
            help.Margin = new Thickness(2, 0, 0, 8);
            help.Tag = "no-tooltip";
            help.FontSize = 15;
            Body.Children.Add(help);

            _conflicts = new InfoBar { IsClosable = false, IsOpen = false, Severity = InfoBarSeverity.Warning, Margin = new Thickness(0, 0, 0, 8) };
            Body.Children.Add(_conflicts);
            var bindingCard=new Card { Content=BloodborneLauncher.Core.UiText.T(_table), Padding=new Thickness(12,8,12,10) };
            MenuSurface.SetKeybindingsShadow(bindingCard,true);
            Body.Children.Add(bindingCard);

            // ==================== Capture overlay ====================
            _captureTitle = Text("", "PageTitle");
            _captureHelp = Text("", "Muted");
            _captureHelp.Margin = new Thickness(0, 4, 0, 12);
            _captureHelp.FontSize = 16;
            _captureFeedback = Text("", "Muted");
            _captureFeedback.FontSize = 16;
            _captureFeedback.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");

            _captureBoxText = Text("", "Faint");
            _captureBoxText.FontSize = 16;
            _captureBoxText.HorizontalAlignment = HorizontalAlignment.Center;
            _captureBoxText.VerticalAlignment = VerticalAlignment.Center;
            _captureBoxText.TextAlignment = TextAlignment.Center;
            var capturePad = new Border
            {
                Height = 110,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Child = _captureBoxText,
                Tag = "capture-wheel",
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 10),
            };
            capturePad.SetResourceReference(Border.BackgroundProperty, "PanelDeepBrush");
            capturePad.SetResourceReference(Border.BorderBrushProperty, "AccentTextFillColorPrimaryBrush");
            capturePad.PreviewMouseDown += (_, e) =>
            {
                e.Handled = true;
                if (_padInput == null && InputMap.MouseToken(e.ChangedButton) is { } name) CaptureToken(name);
            };
            capturePad.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (_padInput == null)
                {
                    var token = e.Delta > 0 ? "WheelUp" : "WheelDown";
                    CaptureToken(token);
                    _chordCapture.Release(token);
                }
            };

            // Controller capture: the button pressed on the pad, or one picked from this list.
            _padChoices = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var button in PadMap.Buttons.Where(b => b.Sdl.Length > 0))
            {
                var sdl = button.Sdl;
                var chip = new BloodborneLauncher.Controls.Button { Content = PadMap.DisplayLabel(button.Sdl), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 4), FontSize = 15 };
                chip.Click += (_, _) => CommitPad(sdl);
                _padChoices.Children.Add(chip);
            }
            _padTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _padTimer.Tick += (_, _) => PollPad();

            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            _addBindingButton = SmallButton(UiText.T("Add input"), SymbolRegular.Add24, (_, _) => {
                if (_target == null || _padInput != null) return;
                _addMode = true;
                _captureHelp.Text = UiText.T("The next input is added to this action without replacing its current bindings.");
            });
            buttons.Children.Add(_addBindingButton);
            buttons.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Unbind"), SymbolRegular.Dismiss24, (_, _) => { if (_padInput != null) CommitPad(""); else Commit(null); }));
            buttons.Children.Add(SmallButton(BloodborneLauncher.Core.UiText.T("Cancel"), SymbolRegular.ArrowExit20, (_, _) => EndCapture()));

            var dialog = new OptionRows();
            dialog.Children.Add(_captureTitle);
            dialog.Children.Add(_captureHelp);
            dialog.Children.Add(capturePad);
            dialog.Children.Add(_padChoices);
            dialog.Children.Add(_captureFeedback);
            dialog.Children.Add(buttons);
            var dialogCard = new Border
            {
                Width = 500,
                Padding = new Thickness(22, 18, 22, 16),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = dialog,
            };
            dialogCard.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            dialogCard.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            var dialogSurface = new Grid();
            dialogCard.Child = null;
            dialogSurface.Children.Add(new MenuControlFrame { IsHitTestVisible = false });
            dialogSurface.Children.Add(dialog);
            dialogCard.Child = dialogSurface;

            _overlay = new Grid { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x12)), Focusable = true };
            _overlay.Children.Add(dialogCard);
            _overlay.MouseDown += (_, e) => { if (e.OriginalSource == _overlay) EndCapture(); };

            AttachOverlay(_overlay);
            Unloaded += (_, _) => EndCapture();

            var status = _statusPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            status.Tick += (_, _) => { if (IsVisible && Window.GetWindow(this)?.IsActive==true) UpdatePadStatus(); };
            // A receiver stays connected while its pad is off: only input changing shows the pad is on.
            var activity = _activityPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            activity.Tick += (_, _) =>
            {
                if (IsDisposed || !IsVisible || Window.GetWindow(this)?.IsActive!=true || Ws.Game.Running || _target != null || _padInput != null ||
                    Ini.Get("input_mode","keyboard_mouse")!="controller" || !SdlPad.Available) return;
                var state = SdlPad.State();
                if (_padState != null && state != _padState)
                {
                    var first = _padSeen == null || DateTime.Now - _padSeen > TimeSpan.FromMinutes(2);
                    _padSeen = DateTime.Now;
                    if (first) UpdatePadStatus();
                }
                _padState = state;
            };
            Action portChanged = () => { Rebuild(); FillProfiles(); };
            Ws.PortChanged += portChanged;
            ReleaseWhenDisposed(() => Ws.PortChanged -= portChanged);
            ReleaseWhenDisposed(() => { status.Stop(); activity.Stop(); _padTimer.Stop(); _chordTimer.Stop(); EndCapture(); SdlPad.Stop(); });
            Loaded += (_, _) => UpdatePadMonitoring();
            Action gameStateChanged=()=> { UpdatePadMonitoring();UpdatePadStatus(); };
            Ws.GameStateChanged+=gameStateChanged;
            ReleaseWhenDisposed(()=>Ws.GameStateChanged-=gameStateChanged);
            Unloaded += (_, _) => { status.Stop(); activity.Stop(); SdlPad.Stop(); };
            _chordTimer.Tick+=(_,_)=>PollChord();
            Rebuild();
            FillProfiles();
        }

        public override void OnShown()
        {
            _inputSelector.Select(Ini.Get("input_mode","keyboard_mouse")=="controller"?1:0,notify:false);
            UpdatePadStatus();
            Rebuild();
            FillProfiles();
        }

        private void UseInputMode(bool keyboard){
            Ini.Set("input_mode",keyboard?"keyboard_mouse":"controller");Ini.Set("pc_controls","1");
            Ini.Set("mouse_camera",keyboard?"1":"0");Ini.Set("mouse_menu","1");
            Ini.Set("button_prompts",keyboard?"keyboard":"controller");
            if(_mouseCameraToggle!=null)_mouseCameraToggle.IsChecked=keyboard;
            if(keyboard)SdlPad.Stop();
            UpdatePadStatus();
            Changed();
        }
        private void RefreshDeviceSections()
        {
            bool keyboard = Ini.Get("input_mode", "keyboard_mouse") != "controller";
            _mouseSection.Visibility = keyboard ? Visibility.Visible : Visibility.Collapsed;
            _gamepadOptions.Visibility = keyboard ? Visibility.Collapsed : Visibility.Visible;
            _inputSelector.Select(keyboard ? 0 : 1, notify: false);
        }
        private static IEnumerable<string> HeldCaptureInputs()
        {
            // WPF's Key enum also includes routing/IME sentinels. Poll real,
            // bindable keys only; lock toggles are not treated as held inputs.
            foreach (var key in CaptureKeys)
                if (Keyboard.IsKeyDown(key)) yield return BindingExpressions.CaptureToken(InputMap.SdlKeyName(key)!);
            if (Mouse.LeftButton == MouseButtonState.Pressed) yield return "Mouse1";
            if (Mouse.MiddleButton == MouseButtonState.Pressed) yield return "Mouse2";
            if (Mouse.RightButton == MouseButtonState.Pressed) yield return "Mouse3";
            if (Mouse.XButton1 == MouseButtonState.Pressed) yield return "Mouse4";
            if (Mouse.XButton2 == MouseButtonState.Pressed) yield return "Mouse5";
        }

        private void CaptureToken(string name)
        {
            if (_target == null) return;
            name = BindingExpressions.CaptureToken(name);
            if (InputMap.Reserved.ContainsKey(name)) { _captureFeedback.Text = InputMap.Reserved[name]; return; }
            _chordCapture.RefreshHeld(HeldCaptureInputs());
            _chordCapture.Press(name);
            _captureBoxText.Text = BloodborneLauncher.Core.UiText.T(InputMap.Display(string.Join(" + ", _chordCapture.Tokens)));
        }

        private void PollChord()
        {
            if (_target == null) { _chordTimer.Stop(); return; }
            // Release events normally complete capture immediately. Polling is
            // a fallback for a release outside the pad's hit-test rectangle.
            _chordCapture.RefreshHeld(HeldCaptureInputs());
            FinishChord();
        }

        private void FinishChord()
        {
            if (_target == null || _chordCapture.TakeCompleted() is not { } tokens) return;
            var value = InputMap.Canonical(string.Join(" + ", tokens));
            if (value != null) Commit(value);
        }

        private void AttachCaptureInput()
        {
            _captureWindow = Window.GetWindow(this);
            _captureInputHost = (UIElement?)_captureWindow ?? this;
            // The window sees key events even when a child retained focus or
            // marked them handled. Handlers exist only while this dialog is open.
            _captureInputHost.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Page_PreviewKeyDown), true);
            _captureInputHost.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(Capture_PreviewKeyUp), true);
            _captureInputHost.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Capture_PreviewMouseUp), true);
            if (_captureWindow != null) _captureWindow.Deactivated += Capture_Deactivated;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_overlay.Visibility != Visibility.Visible) return;
                _overlay.Focus();
                Keyboard.Focus(_overlay);
            }));
        }

        private void Capture_Deactivated(object? sender, EventArgs e) => EndCapture();

        private static Key CaptureKey(KeyEventArgs e) => e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

        private void Capture_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (_target == null) return;
            e.Handled = true;
            if (InputMap.SdlKeyName(CaptureKey(e)) is { } token)
                _chordCapture.Release(BindingExpressions.CaptureToken(token));
            FinishChord();
        }

        private void Capture_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_target == null) return;
            if (InputMap.MouseToken(e.ChangedButton) is { } token) _chordCapture.Release(token);
            // Let Cancel/Unbind finish their normal Click handler first.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FinishChord));
        }

        private void UpdatePadMonitoring()
        {
            bool monitor=!IsDisposed && IsLoaded && IsVisible && !Ws.Game.Running &&
                Ini.Get("input_mode","keyboard_mouse")=="controller";
            if(monitor) { _statusPoll?.Start();_activityPoll?.Start(); }
            else { _statusPoll?.Stop();_activityPoll?.Stop();if(_padInput==null)SdlPad.Stop(); }
        }

        private void SetPadName(string name)
        {
            // Device-provided names are literal and must not pass through localization.
            _padStatus.Text = name;
            _padStatus.Visibility = string.IsNullOrEmpty(name) ? Visibility.Collapsed : Visibility.Visible;
        }
        private void UpdatePadStatus()
        {
            if (IsDisposed) return;
            if (Ini.Get("input_mode", "keyboard_mouse") != "controller" && _padInput == null)
            {
                SdlPad.Stop(); SetPadName(""); return;
            }
            var devices = Ws.Game.Running ? null : SdlPad.Devices();
            if (devices != null)
            {
                SetPadName(devices.Count == 0 ? UiText.T("Not connected")
                    : string.Join("\n", devices.Select(d => d.Name).Distinct()));
                return;
            }
            var pads = Gamepads.Detect();
            SetPadName(pads.Count == 0 ? UiText.T("Not connected")
                : string.Join("\n", pads.Select(p => p.Name).Distinct()));
        }

        // ---------------------------------------------------------------- the table

        private Dictionary<string, List<ControlDef>> Usage()
        {
            var usage = new Dictionary<string, List<ControlDef>>(StringComparer.OrdinalIgnoreCase);
            foreach (var control in InputMap.Controls)
                foreach (var name in Bindings.Get(Ini, control))
                {
                    if (!usage.TryGetValue(name, out var list)) usage[name] = list = new List<ControlDef>();
                    list.Add(control);
                }
            return usage;
        }

        private string BindingsStamp() => Paths.PortDir+"\n"+S.Str("controls_profile")+"\n"+_search.Text+"\n"+
            string.Join("\n",Ini.Values.Where(pair=>pair.Key.StartsWith("bind.",StringComparison.Ordinal)||
                pair.Key.StartsWith("pad",StringComparison.Ordinal)||pair.Key is "input_mode" or "ds3_profile" or "pc_controls" or "menu_language")
                .OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>pair.Key+"="+pair.Value));

        private void Rebuild()
        {
            var stamp=BindingsStamp();
            if (_tableStamp==stamp) return;
            _tableStamp=stamp;
            RefreshDeviceSections();
            _table.Children.Clear();
            // Keyboard and mouse follow the PC controls switch; the controller column always works.
            var pcControls = Ini.Get("pc_controls", "1") != "0";
            var usage = Usage();
            var filter = _search.Text.Trim();

            _table.Children.Add(TableRow(Header("Action"), Header("Keyboard & mouse"), Header("Gamepad"), new FrameworkElement(), header: true));

            foreach (var group in InputMap.Controls.GroupBy(c => c.Group))
            {
                var rows = group.Where(c => Matches(c, filter)).ToList();
                if (rows.Count == 0) continue;

                var groupLabel = Text(UiText.T(group.Key).ToUpper(System.Globalization.CultureInfo.GetCultureInfo(UiText.Locale)), "StatLabel");
                groupLabel.FontWeight = FontWeights.SemiBold;
                groupLabel.FontSize = 14;
                groupLabel.Margin = new Thickness(4, 14, 0, 4);
                _table.Children.Add(groupLabel);

                foreach (var control in rows)
                {
                    var name = new OptionRows { VerticalAlignment = VerticalAlignment.Center };
                    name.Children.Add(new TextBlock { Text = BloodborneLauncher.Core.UiText.T(control.Label), FontSize = 16, TextWrapping = TextWrapping.Wrap });

                    var c = control;
                    var reset = new Button
                    {
                        Icon = Icon(SymbolRegular.ArrowUndo24),
                        Appearance = ControlAppearance.Transparent,
                        Padding = new Thickness(6),
                        ToolTip = null,
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = Bindings.IsDefault(Ini, control) && (PadMap.InputFor(control) is not { } padInput || PadMap.IsDefault(Ini, padInput))
                            ? Visibility.Hidden : Visibility.Visible,
                    };
                    reset.Click += (_, _) =>
                    {
                        if (PadMap.InputFor(c) is { } input) PadMap.Reset(Ini, input);
                        Store(c, InputMap.Split(c.Default));
                    };

                    var keys = Cell(control, usage);
                    keys.IsEnabled = pcControls;
                    _table.Children.Add(TableRow(name, keys, PadCell(control), reset));
                }
            }

            var shared = usage.Where(u => u.Value.Count > 1).Select(u => $"{InputMap.Display(u.Key)} ({string.Join(", ", u.Value.Select(c => c.Label))})").ToList();
            var reserved = usage.Keys.Where(InputMap.Reserved.ContainsKey).Select(k => InputMap.Reserved[k]).ToList();
            var lines = new List<string>();
            if (shared.Count > 0) lines.Add("Used by more than one action: " + string.Join("; ", shared));
            if (reserved.Count > 0) lines.Add("Also used by the port: " + string.Join("; ", reserved));
            _conflicts.IsOpen = lines.Count > 0;
            _conflicts.Title = shared.Count > 0 ? BloodborneLauncher.Core.UiText.T("Shared inputs") : BloodborneLauncher.Core.UiText.T("Reserved keys");
            _conflicts.Message = BloodborneLauncher.Core.UiText.T(string.Join("\n", lines));
        }

        private static string Show(string value) =>
            InputMap.Split(value) is { Count: > 0 } names ? string.Join(", ", names.Select(InputMap.Display)) : "not bound";

        private bool Matches(ControlDef control, string filter)
        {
            if (filter.Length == 0) return true;
            bool Has(string s) => s.Contains(filter, StringComparison.OrdinalIgnoreCase);
            return Has(control.Label) || Has(control.Hint) || (control.Pad != null && Has(control.Pad)) ||
                   Bindings.Get(Ini, control).Any(n => Has(n) || Has(InputMap.Display(n)));
        }

        private static TextBlock Header(string text)
        {
            var block = Text(BloodborneLauncher.Core.UiText.T(text), "StatLabel");
            block.FontWeight = FontWeights.SemiBold;
            block.FontSize = 15;
            block.TextWrapping = TextWrapping.Wrap;
            return block;
        }

        private static Grid TableRow(FrameworkElement name, FrameworkElement keys, FrameworkElement pad, FrameworkElement action, bool header = false)
        {
            var grid = new Grid { Margin = new Thickness(0, header ? 4 : 1, 0, header ? 2 : 1), MinHeight = header ? 0 : 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            name.Margin = new Thickness(4, 0, 8, 0);
            Grid.SetColumn(keys, 1);
            Grid.SetColumn(pad, 2);
            Grid.SetColumn(action, 3);
            keys.Margin = new Thickness(header ? 10 : 4, 0, 4, 0);
            pad.Margin = new Thickness(10, 0, 0, 0);
            grid.Children.Add(name);
            grid.Children.Add(keys);
            grid.Children.Add(pad);
            grid.Children.Add(action);
            return grid;
        }

        /// <summary>One action's keyboard inputs, using the native menu button style.</summary>
        private FrameworkElement Cell(ControlDef control, Dictionary<string, List<ControlDef>> usage)
        {
            var tokens = Bindings.Get(Ini, control).Select(name =>
            {
                var others = usage.TryGetValue(name, out var list) ? list.Where(c => c != control).Select(c => c.Label).ToArray() : Array.Empty<string>();
                var reserved = InputMap.Reserved.GetValueOrDefault(name);
                var warnings = new[] { others.Length > 0 ? "Also bound to: " + string.Join(", ", others) : null, reserved }.Where(t => t != null);
                string? warning = others.Length > 0 || reserved != null ? string.Join("\n", warnings) : null;
                return (Value: name, Label: InputMap.Display(name), Mouse: InputMap.IsMouse(name), Warning: warning);
            });
            return new MenuBindingCell(control.Label, tokens, !Bindings.IsDefault(Ini, control),
                () => BeginCapture(control), () => Store(control, new List<string>()));
        }

        /// <summary>The controller column: the button that does this action, changeable when it is a PS4 button.</summary>
        private FrameworkElement PadCell(ControlDef control)
        {
            if (PadMap.InputFor(control) is not { } input)
            {
                var label = control.Pad ?? "—";
                if (Ini.Flag("pad_swap_sticks"))
                    label = label == "Left stick" ? "Right stick" : label == "Right stick" ? "Left stick" : label;
                var text = Text(BloodborneLauncher.Core.UiText.T(label), "Faint");
                text.VerticalAlignment = VerticalAlignment.Center;
                if (label is "Left stick" or "Right stick")
                {
                    // Stick axes are descriptive, not rebindable buttons: no underline or hit target.
                    text.SetResourceReference(TextBlock.FontFamilyProperty, "BloodborneFont");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                    text.FontSize = 15;
                    text.Margin = new Thickness(8, 3, 10, 3);
                }
                return text;
            }
            // A chip like the keyboard ones: click and press the button on the pad (a drop-down here
            // opened under the pointer and picked whatever the release landed on).
            var current = PadMap.Main(Ini, input);
            var menu = PadMap.IsMenu(input);
            var chip = new BloodborneLauncher.Controls.Button
            {
                Content = current.Length > 0 ? PadMap.DisplayLabel(current) : BloodborneLauncher.Core.UiText.T(menu ? "As in the world" : "Not bound"),
                Icon = Icon(SymbolRegular.XboxController24),
                Padding = new Thickness(8, 3, 10, 3),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = null,
            };
            if (!PadMap.IsDefault(Ini, input)) chip.FontWeight = FontWeights.SemiBold;
            chip.Click += (_, _) => BeginPadCapture(control, input);
            return chip;
        }

        private void Store(ControlDef control, List<string> names)
        {
            Bindings.Set(Ini, control, names);
            Changed();
        }

        private void Changed()
        {
            Ws.SaveSoon();
            Rebuild();
            FillProfiles();UpdatePadMonitoring();
        }

        // ---------------------------------------------------------------- capture

        private void BeginCapture(ControlDef control)
        {
            _target = control;
            _addMode = false;
            _addBindingButton.IsEnabled = Bindings.Get(Ini, control).Count < 5;
            _chordCapture.Begin(HeldCaptureInputs());
            _chordTimer.Start();
            _padInput = null;
            _captureTitle.Text = BloodborneLauncher.Core.UiText.F($"Bind {control.Label}");
            _captureHelp.Text = BloodborneLauncher.Core.UiText.T("Hold keys and mouse buttons, then release all to finish. Esc cancels; Delete clears.");
            _captureBoxText.Text = BloodborneLauncher.Core.UiText.T("Click or scroll here");
            _padChoices.Visibility = Visibility.Collapsed;
            _captureFeedback.Text = "";
            _overlay.Visibility = Visibility.Visible;
            AttachCaptureInput();
        }

        private void EndCapture()
        {
            _overlay.Visibility = Visibility.Collapsed;
            _target = null;
            _padInput = null;
            _padControl = null;
            _padTimer.Stop();
            _chordTimer.Stop();
            _chordCapture.Clear();
            if (_captureInputHost != null)
            {
                _captureInputHost.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Page_PreviewKeyDown));
                _captureInputHost.RemoveHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(Capture_PreviewKeyUp));
                _captureInputHost.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Capture_PreviewMouseUp));
                _captureInputHost = null;
            }
            if (_captureWindow != null) _captureWindow.Deactivated -= Capture_Deactivated;
            _captureWindow = null;
            if (Ini.Get("input_mode","keyboard_mouse")!="controller") SdlPad.Stop();
        }

        private void BeginPadCapture(ControlDef control, string input)
        {
            _addBindingButton.IsEnabled = false;
            _target = null;
            _padControl = control;
            _padInput = input;
            _captureTitle.Text = BloodborneLauncher.Core.UiText.F($"Controller button for {control.Label}");
            var live = !Ws.Game.Running && SdlPad.Available;
            _captureHelp.Text = live
                ? BloodborneLauncher.Core.UiText.T("Press the button on your controller, or pick one below. Esc cancels. A button another action uses swaps over.")
                : Ws.Game.Running ? BloodborneLauncher.Core.UiText.T("Pick a button below (pressing one works while the game is closed).")
                : BloodborneLauncher.Core.UiText.T("Pick a button below.");
            _captureBoxText.Text = live ? BloodborneLauncher.Core.UiText.T("Waiting for a button…") : BloodborneLauncher.Core.UiText.T("Controller input is not available right now");
            _padChoices.Visibility = Visibility.Visible;
            _captureFeedback.Text = "";
            _overlay.Visibility = Visibility.Visible;
            AttachCaptureInput();
            if (live)
            {
                // Buttons already held when the dialog opens don't count until released.
                _padHeld = SdlPad.Held();
                _padTimer.Start();
            }
        }

        private void PollPad()
        {
            if (_padInput == null || Ws.Game.Running) { _padTimer.Stop(); return; }
            var held = SdlPad.Held();
            var pressed = held.Except(_padHeld).FirstOrDefault();
            _padHeld = held;
            if (pressed != null) CommitPad(pressed);
        }

        private void CommitPad(string button)
        {
            if (_padInput == null || _padControl == null) return;
            var input = _padInput;
            var control = _padControl;
            EndCapture();
            PadMap.Assign(Ini, input, button);
            Changed();
            Ws.Log(BloodborneLauncher.Core.UiText.F($"Controller: {control.Label} on {(button.Length > 0 ? PadMap.Label(button) : "nothing")}."), LogLevel.Step);
        }

        /// <summary>name: the captured input (it replaces the one of its kind), or null to unbind.</summary>
        private void Commit(string? name)
        {
            if (_target == null) return;
            var control = _target;
            bool append = _addMode;
            EndCapture();
            if (name == null)
                Bindings.Set(Ini, control, new List<string>());
            else if (append)
                Bindings.Set(Ini, control, Bindings.Get(Ini, control).Append(name));
            else
                Bindings.Assign(Ini, control, name, keepOtherKind: true);
            Changed();
            Ws.Log($"{control.Label}: {Show(InputMap.Join(Bindings.Get(Ini, control)))}", LogLevel.Step);
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_target == null && _padInput == null) return;
            e.Handled = true;
            var key = CaptureKey(e);
            if (_padInput != null)
            {
                if (key == Key.Escape) EndCapture();
                return;
            }
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                EndCapture();
                return;
            }
            if(key==Key.Delete){Commit(null);return;}
            var name = InputMap.SdlKeyName(key);
            if (name == null)
            {
                _captureFeedback.Text = BloodborneLauncher.Core.UiText.F($"{key} cannot be bound; try another key.");
                return;
            }
            if (!e.IsRepeat) CaptureToken(name);
        }

        // ---------------------------------------------------------------- profiles

        private static bool SameBindings(Dictionary<string, string> a, Dictionary<string, string> b) =>
            InputMap.Controls.All(c =>
            {
                var x = InputMap.Split(a.TryGetValue(c.Name, out var va) ? va : c.Default);
                var y = InputMap.Split(b.TryGetValue(c.Name, out var vb) ? vb : c.Default);
                return x.Count == y.Count && x.Zip(y).All(p => p.First.Equals(p.Second, StringComparison.OrdinalIgnoreCase));
            });

        private string CurrentDevice => Ini.Get("input_mode", "keyboard_mouse");
        private bool ProfileMatches(ControlProfiles.Profile profile)
        {
            if (profile.InputDevice != CurrentDevice) return false;
            if (CurrentDevice == "keyboard_mouse") return SameBindings(profile.Bindings, ControlProfiles.Current(Ini));
            var pad = PadMap.Current(Ini);
            return PadMap.Inputs.All(i => string.Equals(profile.Pad?.GetValueOrDefault(i, "") ?? "", pad.GetValueOrDefault(i, ""), StringComparison.OrdinalIgnoreCase));
        }
        private ControlProfiles.Profile? Matching()
        {
            var chosen = S.Str("controls_profile." + CurrentDevice);
            if (chosen.Length == 0) chosen = S.Str("controls_profile");
            return _profiles.FirstOrDefault(p => p.Name == chosen && ProfileMatches(p))
                   ?? _profiles.FirstOrDefault(ProfileMatches);
        }

        private void FillProfiles()
        {
            string files;
            try {
                files=ControlProfiles.Folder+"\n"+string.Join("\n",Directory.Exists(ControlProfiles.Folder)
                    ? Directory.GetFiles(ControlProfiles.Folder,"*.json").OrderBy(file=>file,StringComparer.OrdinalIgnoreCase)
                        .Select(file=>{var info=new FileInfo(file);return file+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;})
                    : Enumerable.Empty<string>());
            } catch(IOException) { files=Guid.NewGuid().ToString(); }
            catch(UnauthorizedAccessException) { files=Guid.NewGuid().ToString(); }
            files += "\n" + CurrentDevice;
            var view=BindingsStamp()+"\n"+files;
            if (_profilesViewStamp==view) return;
            _profilesViewStamp=view;
            _fillingProfiles = true;
            if (_profileFilesStamp!=files) { _profiles=ControlProfiles.All(CurrentDevice);_profileFilesStamp=files; }
            _profileBox.Items.Clear();
            var match = Matching();
            if (match == null)
                _profileBox.Items.Add(new ComboBoxItem { Content = BloodborneLauncher.Core.UiText.T("Custom (not saved)"), Tag = null });
            foreach (var p in _profiles)
                _profileBox.Items.Add(new ComboBoxItem { Content = p.BuiltIn ? UiText.T(p.Name) : p.Name, Tag = p,
                    ToolTip = p.BuiltIn && p.Name == ControlProfiles.DarkSouls3Name ? UiText.T(ControlProfiles.DarkSouls3Help) : null });
            _profileBox.SelectedItem = _profileBox.Items.OfType<ComboBoxItem>().First(i => ReferenceEquals(i.Tag, match));
            SetHint(_profileHint, null);
            MenuHelp.Attach(_profileBox, match is { BuiltIn: true, Name: ControlProfiles.DarkSouls3Name }
                ? UiText.T(ControlProfiles.DarkSouls3Help) : null);
            _deleteProfile.IsEnabled = match is { BuiltIn: false };
            _fillingProfiles = false;
        }

        private void ProfileChosen()
        {
            if (_fillingProfiles || _profileBox.SelectedItem is not ComboBoxItem { Tag: ControlProfiles.Profile profile }) return;
            if (Matching() == null && !Prompt.Confirm(BloodborneLauncher.Core.UiText.T("Replace your bindings?"),
                    BloodborneLauncher.Core.UiText.F($"Your current bindings are not saved in a profile. Using \"{profile.Name}\" replaces them."), BloodborneLauncher.Core.UiText.T("Use profile")))
            {
                _profilesViewStamp = null;
                FillProfiles();
                return;
            }
            ControlProfiles.Apply(S, Ini, profile);
            _pcToggle.IsChecked = Ini.Get("pc_controls", "1") != "0";
            _mouseOptions.IsEnabled = _pcToggle.IsChecked == true;
            _mouseCameraToggle.IsChecked = Ini.Get("mouse_camera", "1") != "0";
            _mouseMenuToggle.IsChecked = Ini.Get("mouse_menu", "1") != "0";
            Ws.Log(BloodborneLauncher.Core.UiText.F($"Control profile: {profile.Name}"), LogLevel.Step);
            Changed();
        }

        private ControlProfiles.Profile CurrentAsProfile()
        {
            var match = Matching();
            var remembered = S.Str("controls_profile." + CurrentDevice);
            var name = match?.Name ?? (remembered.Length > 0 && !ControlProfiles.BuiltIns.Any(p => p.Name == remembered) ? remembered : "My controls");
            return new ControlProfiles.Profile(
                name,
                match?.Author ?? S.Str("profile_author"),
                match?.Description ?? "",
                CurrentDevice == "keyboard_mouse" ? ControlProfiles.Current(Ini) : new(), BuiltIn: match?.BuiltIn ?? false,
                Pad: CurrentDevice == "controller" ? PadMap.Current(Ini) : null, InputDevice: CurrentDevice);
        }

        private void SaveProfile()
        {
            var current = CurrentAsProfile();
            var values = Prompt.Ask(BloodborneLauncher.Core.UiText.T("Save profile"), BloodborneLauncher.Core.UiText.T("Saves your current bindings so you can switch back to them or share them."),
                new Prompt.Field(BloodborneLauncher.Core.UiText.T("Name"), current.BuiltIn ? "" : current.Name, BloodborneLauncher.Core.UiText.T("My controls")),
                new Prompt.Field(BloodborneLauncher.Core.UiText.T("Author"), S.Str("profile_author"), BloodborneLauncher.Core.UiText.T("Optional")),
                new Prompt.Field(BloodborneLauncher.Core.UiText.T("Description"), current.Description, BloodborneLauncher.Core.UiText.T("What the layout is good for (optional)"), Multiline: true));
            if (values == null) return;

            var name = values[0].Trim();
            if (name.Length == 0) name = "My controls";
            if (ControlProfiles.BuiltIns.Any(b => (b.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || UiText.T(b.Name).Equals(name, StringComparison.OrdinalIgnoreCase))))
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Choose another name"), BloodborneLauncher.Core.UiText.F($"\"{name}\" is a built-in profile."));
                return;
            }
            if (_profiles.Any(p => !p.BuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                !Prompt.Confirm(BloodborneLauncher.Core.UiText.T("Replace the profile?"), BloodborneLauncher.Core.UiText.F($"A profile called \"{name}\" already exists."), BloodborneLauncher.Core.UiText.T("Replace")))
                return;

            S.Set("profile_author", values[1].Trim());
            var profile = new ControlProfiles.Profile(name, values[1].Trim(), values[2].Trim(), CurrentDevice == "keyboard_mouse" ? ControlProfiles.Current(Ini) : new(), Pad: CurrentDevice == "controller" ? PadMap.Current(Ini) : null, InputDevice: CurrentDevice);
            profile = profile with { File = _profiles.FirstOrDefault(p => !p.BuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.File ?? "" };
            try
            {
                ControlProfiles.Save(profile);
                S.Set("controls_profile", name);
                S.Set("controls_profile." + CurrentDevice, name);
                Ws.SaveSoon();
                Ws.Log(BloodborneLauncher.Core.UiText.F($"Saved control profile \"{name}\"."), LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Could not save the profile: {0}",e.Message), LogLevel.Bad);
            }
            FillProfiles();
        }

        private void DeleteProfile()
        {
            if (_profileBox.SelectedItem is not ComboBoxItem { Tag: ControlProfiles.Profile { BuiltIn: false } profile }) return;
            if (!Prompt.Confirm(BloodborneLauncher.Core.UiText.T("Delete the profile?"), BloodborneLauncher.Core.UiText.F($"\"{profile.Name}\" is removed from this PC. Your current bindings stay as they are."), BloodborneLauncher.Core.UiText.T("Delete")))
                return;
            try
            {
                ControlProfiles.Delete(profile);
                Ws.Log(BloodborneLauncher.Core.UiText.F($"Deleted control profile \"{profile.Name}\"."), LogLevel.Step);
            }
            catch (Exception e)
            {
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Could not delete the profile: {0}",e.Message), LogLevel.Bad);
            }
            FillProfiles();
        }

        private void CopyProfile()
        {
            Clipboard.SetText(ControlProfiles.ToJson(CurrentAsProfile()));
            Ws.Log(BloodborneLauncher.Core.UiText.T("Profile JSON copied. Paste it on any paste site and share the link."), LogLevel.Good);
        }

        private void ExportProfile()
        {
            var profile = CurrentAsProfile();
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = BloodborneLauncher.Core.UiText.T("Export control profile"), FileName = profile.Name + ".json", Filter = "Control profile|*.json" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                System.IO.File.WriteAllText(dialog.FileName, ControlProfiles.ToJson(profile));
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Profile exported: {0}",dialog.FileName), LogLevel.Good);
            }
            catch (Exception e)
            {
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Could not export the profile: {0}",e.Message), LogLevel.Bad);
            }
        }

        private void ImportFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = BloodborneLauncher.Core.UiText.T("Import control profile"), Filter = "Control profile|*.json|All files|*.*" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                Offer(ControlProfiles.Parse(System.IO.File.ReadAllText(dialog.FileName)));
            }
            catch (Exception e)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Could not import"), BloodborneLauncher.Core.UiText.T(e.Message));
            }
        }

        private async Task ImportLinkAsync()
        {
            var values = Prompt.Ask(BloodborneLauncher.Core.UiText.T("Import from a link"),
                BloodborneLauncher.Core.UiText.Format("Paste a link to a shared profile: a Pastebin, GitHub Gist or file, Rentry, hastebin, paste.rs, pastes.dev or dpaste link, or any link that shows the raw JSON."),
                new Prompt.Field(BloodborneLauncher.Core.UiText.T("Link"), "", "https://pastebin.com/…"));
            if (values == null || values[0].Trim().Length == 0) return;
            try
            {
                Ws.Log(BloodborneLauncher.Core.UiText.T("Downloading the profile…"), LogLevel.Step);
                Offer(await ControlProfiles.DownloadAsync(values[0]));
            }
            catch (Exception e)
            {
                Prompt.Tell(BloodborneLauncher.Core.UiText.T("Could not import"), e is FormatException ? BloodborneLauncher.Core.UiText.T(e.Message) : BloodborneLauncher.Core.UiText.Format("The link could not be read: {0}",e.GetBaseException().Message));
            }
        }

        /// <summary>Shows what an imported profile changes and, if accepted, saves and uses it.</summary>
        private void Offer(ControlProfiles.Parsed parsed)
        {
            var profile = parsed.Profile;
            var current = ControlProfiles.Current(Ini);
            var changes = InputMap.Controls
                .Where(c => !SameBindings(new() { [c.Name] = profile.Bindings.GetValueOrDefault(c.Name, c.Default) }, new() { [c.Name] = current[c.Name] }))
                .Select(c => $"  {c.Label}: {Show(profile.Bindings.GetValueOrDefault(c.Name, c.Default))}")
                .ToList();

            if (profile.InputDevice == "controller")
            {
                var currentPad = PadMap.Current(Ini);
                changes = PadMap.Inputs.Where(i => (profile.Pad?.GetValueOrDefault(i, "") ?? "") != currentPad.GetValueOrDefault(i, ""))
                    .Select(i => "  " + i + ": " + (profile.Pad?.GetValueOrDefault(i, "") ?? "")).ToList();
            }
            var text = new List<string>();
            if (profile.Author.Length > 0) text.Add("by " + profile.Author);
            if (profile.Description.Length > 0) text.Add(profile.Description);
            text.Add(changes.Count == 0
                ? "It is the same as your current bindings."
                : BloodborneLauncher.Core.UiText.F($"Changes {changes.Count} of {InputMap.Controls.Length} actions:\n") + string.Join("\n", changes.Take(12)) + (changes.Count > 12 ? "\n  …" : ""));
            if (parsed.Notes.Count > 0)
                text.Add("Notes:\n  " + string.Join("\n  ", parsed.Notes.Take(6)));

            if (!Prompt.Confirm(BloodborneLauncher.Core.UiText.F($"Import \"{profile.Name}\"?"), BloodborneLauncher.Core.UiText.T(string.Join("\n\n", text)), BloodborneLauncher.Core.UiText.T("Save and use")))
                return;

            var name = profile.Name;
            var destinationProfiles = ControlProfiles.All(profile.InputDevice);
            for (var i = 2; destinationProfiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
                name = $"{profile.Name} ({i})";
            profile = profile with { Name = name };
            try
            {
                ControlProfiles.Save(profile);
            }
            catch (Exception e)
            {
                Ws.Log(BloodborneLauncher.Core.UiText.Format("Could not save the imported profile: {0}",e.Message), LogLevel.Bad);
            }
            ControlProfiles.Apply(S, Ini, profile);
            _pcToggle.IsChecked = Ini.Get("pc_controls", "1") != "0";
            _mouseOptions.IsEnabled = _pcToggle.IsChecked == true;
            _mouseCameraToggle.IsChecked = Ini.Get("mouse_camera", "1") != "0";
            _mouseMenuToggle.IsChecked = Ini.Get("mouse_menu", "1") != "0";
            Ws.Log(BloodborneLauncher.Core.UiText.F($"Imported control profile \"{name}\"."), LogLevel.Good);
            Changed();
        }
    }
}
