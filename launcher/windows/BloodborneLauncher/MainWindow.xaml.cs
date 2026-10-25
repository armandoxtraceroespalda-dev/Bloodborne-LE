using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BloodborneLauncher.Core;
using BloodborneLauncher.Views;
using BloodborneLauncher.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BloodborneLauncher
{
    public partial class MainWindow
    {
        private readonly PageCache<Type, LauncherPage> _pages = new(1, page => page.Dispose());
        private bool _closing;

        public static MainWindow? Instance => Application.Current.MainWindow as MainWindow;

        public MainWindow()
        {
            using (LauncherDiagnostics.Measure("Main window XAML")) InitializeComponent();
            SetWindowChrome();
            _navigationMemory=new NavigationMemoryCoordinator(this,()=>_navigationInProgress || Mouse.Captured!=null ||
                !IsEnabled || PageFrame.Content is ControlsPage {IsCapturingInput:true});
            SourceInitialized+=(_,_)=>{_backdropReady=true;RefreshBackdrop();};
            SizeChanged+=(_,_)=>{if(_backdropReady)RefreshBackdrop();};
            DpiChanged+=(_,_)=>RefreshBackdrop();

            Workspace ws;
            using (LauncherDiagnostics.Measure("Shared workspace creation")) ws = Workspace.Current;
            ws.Logged += LauncherDiagnostics.Record;
            ws.GameStateChanged += UpdatePlayButton;
            ws.GameCrashed += OnGameCrashed;
            using (LauncherDiagnostics.Measure("Workspace initialization")) ws.Initialise();
            CheatAccess.Session.Restore(ws.Settings.Bool("cheats_unlocked"));
            CheatInputRouter.Register();
            MenuSounds.Attach(this);
            UpdateAudioToggle();

            // One presentation: retain the existing preference key for source compatibility.
            var args = Environment.GetCommandLineArgs();
            ws.Settings.Set("ui_layout", "bloodborne");
            ApplyBloodborne();
            using (LauncherDiagnostics.Measure("Sidebar construction")) BuildSidebar();
            SizeChanged+=(_,_)=>ScaleSidebar();
            if (Array.IndexOf(args, "--test-prompt") is var shot and >= 0 && shot + 1 < args.Length)
            {
                Prompt.SnapshotPath = args[shot + 1];
                ShowActivated = false;
                ShowInTaskbar = false;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -32000;
                Top = -32000;
            }
            // --play (Bloodborne.exe): the game straight away with the saved settings, the launcher
            // out of sight meanwhile (minimized, then hidden: its prompts show on their own, in the
            // middle of the screen, and it has no taskbar button to close the game by).
            _directPlay = args.Contains("--play") && !RenderAudit.Enabled;
            if (_directPlay)
                WindowState = WindowState.Minimized;


// No update notification or release download is active in this fork.
#if false // Intentionally disabled; retained as upstream reference.
            ws.UpdateStateChanged += () =>
            {
                TitleUpdate.Visibility = ws.Update != null ? Visibility.Visible : Visibility.Collapsed;
                if (ws.Update != null) TitleUpdateText.Text = $"Update {ws.Update.Tag}";
            };

#endif

            Loaded += async (_, _) =>
            {
// Do not initialize release checks, channels or update cleanup at startup.
#if false // Intentionally disabled; retained as upstream reference.
                Updater.CleanUp();
                // Running a beta means choosing beta: kept after updating to a stable release.
                if (Updater.CurrentBeta > 0 && ws.Settings.Str("update_channel").Length == 0)
                {
                    ws.Settings.Set("update_channel", "beta");
                    ws.SaveSoon();
                }
                if (ws.Settings.Bool("check_updates"))
                    _ = ws.CheckForUpdatesAsync(quiet: true);
#endif

                await Task.Yield();
                GoHome();

                UpdatePlayButton();
                // --test-trophy: a sample notification, to check how it looks over the game.
                if (Environment.GetCommandLineArgs().Contains("--test-trophy"))
                    TrophyToast.Announce(TrophyTracker.ById[14]);
                // --test-prompt <png>: a picture of a sample dialog over the launcher, drawn off-screen.
                // --test-import: the import dialog with a long pasted code instead.
                if (Prompt.SnapshotPath != null && Environment.GetCommandLineArgs().Contains("--test-import"))
                    Dispatcher.BeginInvoke(() => Prompt.Ask(BloodborneLauncher.Core.UiText.T("Import settings"), BloodborneLauncher.Core.UiText.F($"Paste the code someone shared (it starts with {SharedSettings.CodePrefix})."),
                            new Prompt.Field(BloodborneLauncher.Core.UiText.T("Code"), SharedSettings.ToCode(SharedSettings.Take(ws.Ini, ws.Settings)), "", Multiline: true)),
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                else if (Prompt.SnapshotPath != null)
                    Dispatcher.BeginInvoke(() => Prompt.Confirm(BloodborneLauncher.Core.UiText.T("Switch layout"), BloodborneLauncher.Core.UiText.T("The launcher restarts to change its layout."), BloodborneLauncher.Core.UiText.T("Restart now")),
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                ws.Log(Paths.IsPortDir(Paths.PortDir)
                        ? BloodborneLauncher.Core.UiText.F($"Bloodborne Launcher ready. Engine: {Paths.PortDir}")
                        : BloodborneLauncher.Core.UiText.T("Bloodborne Launcher ready. Choose the engine folder in Settings."),
                    Paths.IsPortDir(Paths.PortDir) ? LogLevel.Good : LogLevel.Warn);
                // Once: the settings that suit this PC, shown before anything changes.
                if (_directPlay)
                    _ = Dispatcher.BeginInvoke(async () => await PlayDirectAsync(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                else if (!RenderAudit.Enabled && !RetentionAudit.Enabled && !ws.Settings.Bool("hardware_offered") && Prompt.SnapshotPath == null && Paths.IsPortDir(Paths.PortDir))
                    _ = Dispatcher.BeginInvoke(() => OfferBestSettings(firstRun: true), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            // Pointer input has no cleanup hooks; only settled navigation can queue a bounded attempt.
            ContentRendered+=FirstContentRendered;
            Closed += (_, _) => {
                _closed=true;
                _shownRefresh?.Abort(); _shownRefresh=null;
                _navigationCancel?.Cancel(); _navigationCancel?.Dispose(); _navigationCancel=null;
                MenuTransition.Reset(PageFrame);
                _retentionAudit?.Dispose(); _retentionAudit=null;
                _navigationMemory.Dispose();
                ws.GameStateChanged-=UpdatePlayButton;
                ws.GameCrashed-=OnGameCrashed;
                ws.Logged-=LauncherDiagnostics.Record;
                try { _pages.Clear(); }
                finally { PageFrame.Content=null;BackdropImage.Source=null; }
            };
            Closing += OnClosing;
        }

        private void FirstContentRendered(object? sender,EventArgs args)
        {
            ContentRendered-=FirstContentRendered;
            _retentionAudit=RetentionAudit.Start(this,()=>PageFrame.Content?.GetType().Name??"none",()=>_pages.Count);
            RenderAudit.Start(this);
            LauncherDiagnostics.Memory("First content rendered");
            LauncherDiagnostics.Record(BloodborneLauncher.Core.UiText.F($"Wallpaper viewport: source={BackdropImage.Source!=null}; visible={BackdropImage.IsVisible}; render tier={RenderCapability.Tier >> 16}; size={BackdropImage.ActualWidth:0}x{BackdropImage.ActualHeight:0}; opacity={BackdropImage.Opacity:0.00}; shade={((BackdropShade.Fill as SolidColorBrush)?.Color.A ?? 0)/255.0:0.00}; settings shade={SettingsShade.Visibility}"),LogLevel.Info);
        }

        private static readonly Brush HomeShade = FrozenShade(38), PageShade = FrozenShade(140);
        private static Brush FrozenShade(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)); brush.Freeze(); return brush;
        }
        private void AudioToggle_Click(object sender, RoutedEventArgs args)
        {
            MenuSounds.SetMuted(!MenuSounds.Muted);
            Workspace.Current.Settings.Set("menu_sound_muted", MenuSounds.Muted);
            Workspace.Current.SaveSoon(); UpdateAudioToggle();
        }
        private void UpdateAudioToggle()
        {
            AudioMutedMark.Visibility = MenuSounds.Muted ? Visibility.Visible : Visibility.Collapsed;
            AudioWaves.Visibility = MenuSounds.Muted ? Visibility.Collapsed : Visibility.Visible;
            AudioToggle.ToolTip=BloodborneLauncher.Core.UiText.T(MenuSounds.Muted?"Enable launcher sounds":"Mute launcher sounds");
            System.Windows.Automation.AutomationProperties.SetName(AudioToggle, (string)AudioToggle.ToolTip);
        }
        private bool _directPlay, _directCrashed;

        /// <summary>--play: starts the game; the launcher closes when it ends normally, and opens when
        /// the start did not happen or the game crashed (the crash dialog first).</summary>
        private async Task PlayDirectAsync()
        {
            var ws = Workspace.Current;
            Hide();
            await PlayOrStopAsync();
            if (!ws.Game.Running)
            {
                _directPlay = false;
                ShowLauncher();
                return;
            }
            ws.GameCrashed += (_, _) => _directCrashed = true;
            ws.GameStateChanged += () =>
            {
                if (!_directPlay || ws.Game.Running) return;
                _directPlay = false;
                if (_directCrashed)
                    ShowLauncher();
                else
                    Close();
            };
        }

        private void ShowLauncher()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>Shows what would change to suit this PC, and applies it on Apply.</summary>
        public void OfferBestSettings(bool firstRun)
        {
            var ws = Workspace.Current;
            ws.Settings.Set("hardware_offered", true);
            ws.SaveSoon();
            Hardware hw;
            using (LauncherDiagnostics.Measure("Hardware detection")) hw = Hardware.Detect();
            var changes = hw.Recommend(ws.Settings, ws.Ini, out var upscaler);
            var upscalerInfo = $"{PcText.Get("Recommended upscaler")}: {upscaler.Label}\n{PcText.Get(upscaler.Reason)}";
            if (changes.Count == 0)
            {
                if (!firstRun) Prompt.Tell(BloodborneLauncher.Core.UiText.T("Best settings for this PC"), BloodborneLauncher.Core.UiText.F($"{hw.Summary}\n\n{upscalerInfo}\n\nYour settings already suit this PC."));
                return;
            }
            var list = string.Join("\n", changes.Select(c => $"{c.Label}: {c.From} → {c.To}"));
            if (!Prompt.Confirm(firstRun ? BloodborneLauncher.Core.UiText.T("Settings for this PC") : BloodborneLauncher.Core.UiText.T("Best settings for this PC"),
                    BloodborneLauncher.Core.UiText.F($"{hw.Summary}\n\n{upscalerInfo}\n\n{list}\n\nYou can change any of them later."), BloodborneLauncher.Core.UiText.T("Apply")))
                return;
            foreach (var change in changes) change.Apply();
            ws.SaveSoon();
            ws.Log(BloodborneLauncher.Core.UiText.Format("Settings for this PC applied: {0}.",string.Join(", ", changes.Select(c => $"{c.Label.ToLowerInvariant()} {c.To}"))), LogLevel.Good);
            ReloadPages();
        }

        /// <summary>Builds the pages again after settings changed underneath them, staying on the current one.</summary>
        public void ApplyLauncherLanguage()
        {
            var current=PageFrame.Content?.GetType()??typeof(MenuHomePage);
            _navigation.Clear();SidebarItems.Children.Clear();SidebarItems.RowDefinitions.Clear();BuildSidebar();ScaleSidebar();
            WindowsCaptionButtons.Children.OfType<System.Windows.Controls.Button>().First().ToolTip=BloodborneLauncher.Core.UiText.T("Minimize");
            WindowsMaximize.ToolTip=BloodborneLauncher.Core.UiText.T(WindowState==WindowState.Maximized?"Restore":"Maximize");
            WindowsCaptionButtons.Children.OfType<System.Windows.Controls.Button>().Last().ToolTip=BloodborneLauncher.Core.UiText.T("Close");
            UpdateAudioToggle();_pages.Clear();NavigatePage(current);
            SelectSidebar(current==typeof(MenuHomePage)?"home":current==typeof(PlayPage)?"play":
                current==typeof(GraphicsPage)?"graphics":
                current==typeof(ControlsPage)?"controls":current==typeof(TweaksPage)?"tweaks":current==typeof(CheatsPage)?"cheats":
                current==typeof(OnlinePage)?"online":current==typeof(TrophiesPage)?"trophies":current==typeof(MorePage)?"mods":"advanced");
        }

        public void ReloadPages()
        {
            var current = PageFrame.Content as LauncherPage;
            _pages.Clear();
            if (current == null) return;
            if (current is MenuHomePage) { GoHome(); return; }
            NavigatePage(current.GetType());
        }

        /// <summary>The game ended with an error by itself: what happened, safe mode, a report to copy.</summary>
        private void OnGameCrashed(int code, SessionStats session)
        {
            var ws = Workspace.Current;
            var choice = Prompt.Choose(BloodborneLauncher.Core.UiText.T("The game crashed"),
                BloodborneLauncher.Core.UiText.Format("{0}Safe mode starts the next run with the experimental engine options and mods off. Copy report puts the details and the end of the log on the clipboard, for a bug report.",BloodborneLauncher.Core.UiText.F($"{CrashReport.Explain(code)} It ran for {SessionStats.Duration(session.Length)}.\n\n")),
                BloodborneLauncher.Core.UiText.T("Close"), BloodborneLauncher.Core.UiText.T("Copy report"), "Use safe mode");
            if (choice == 1)
            {
                try
                {
                    Clipboard.SetText(CrashReport.Build(ws.Settings, ws.Ini, code));
                    ws.Log(BloodborneLauncher.Core.UiText.T("Crash report copied to the clipboard."), LogLevel.Good);
                }
                catch (Exception e)
                {
                    ws.Log(BloodborneLauncher.Core.UiText.Format("Could not copy the report: {0}",e.Message), LogLevel.Warn);
                }
            }
            else if (choice == 2)
            {
                ws.Settings.Set("safe_mode_next", true);
                ws.SaveSoon();
                ws.Log(BloodborneLauncher.Core.UiText.T("The next start uses safe mode (once)."), LogLevel.Step);
            }
        }

        private void ApplyBloodborne()
        {
            using (LauncherDiagnostics.Measure("Native font and theme")) LauncherAppearance.ApplyBloodborne();
            using (LauncherDiagnostics.Measure("Native image installation")) MenuAssets.Install();
            // Auto keeps compiled app.ico; a chosen launcher picture never replaces the game icon.
            AppIcon.Apply(this, Workspace.Current.Settings.Str("app_icon"));
            AppIcon.SyncShortcuts(Paths.PortDir, Workspace.Current.Settings.Str("app_icon"));
            FontFamily = LauncherAppearance.MenuFont;
            BackdropImage.Source = MenuAssets.Background;
            StateChanged += (_, _) => {
                WindowsMaximize.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
                WindowsMaximize.ToolTip = WindowState == WindowState.Maximized ? BloodborneLauncher.Core.UiText.T("Restore") : BloodborneLauncher.Core.UiText.T("Maximize");
            };
        }

        /// <summary>A wider resize border than FluentWindow's 4px, and the top strip handed back
        /// to Windows as a real caption.</summary>
        private bool _backdropReady;
        private void RefreshBackdrop()
        {
            var dpi=VisualTreeHelper.GetDpi(this);
            int width=BackdropResolutionPolicy.Width(ActualWidth>0?ActualWidth:Width,ActualHeight>0?ActualHeight:Height,dpi.DpiScaleX);
            BackdropImage.Source=MenuAssets.LoadBackground(width);
        }

        private void SetWindowChrome()
        {
            System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome {
                ResizeBorderThickness=new Thickness(8), CaptionHeight=34, CornerRadius=new CornerRadius(0),
                GlassFrameThickness=new Thickness(0), NonClientFrameEdges=System.Windows.Shell.NonClientFrameEdges.None,
                UseAeroCaptionButtons=false
            });
        }

        private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!RenderAudit.Enabled) Workspace.Current.SaveNow();
            if (_closing || !Workspace.Current.Game.Running)
                return;

            var answer = System.Windows.MessageBox.Show(this,
                UiText.T("The game is still running. Stop it and close the launcher?"),
                "Bloodborne Launcher", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                _closing = true;
                await Workspace.Current.StopGameAsync();
                Close();
            }
        }

        // ---------------------------------------------------------------- window buttons

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        // ---------------------------------------------------------------- navigation

        private System.Windows.Threading.DispatcherOperation? _shownRefresh;

        private void RefreshAfterNavigation(LauncherPage page)
        {
            _shownRefresh?.Abort();
            _shownRefresh=Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(() =>
            {
                _shownRefresh=null;
                if (ReferenceEquals(PageFrame.Content,page))
                    using (LauncherDiagnostics.Measure("Refresh " + page.GetType().Name)) page.OnShown();
            }));
        }

        private LauncherPage GetPage(Type type)
        {
            return _pages.GetOrCreate(type, () =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                LauncherPage page;
                using (LauncherDiagnostics.Measure("Construct " + type.Name)) page = (LauncherPage)Activator.CreateInstance(type)!;
                using (LauncherDiagnostics.Measure("Prepare " + type.Name)) page.PrepareForDisplay();
                if (Environment.GetEnvironmentVariable("BB_LAUNCHER_UI_TRACE") == "1")
                    Workspace.Current.Log(BloodborneLauncher.Core.UiText.F($"UI: constructed {type.Name} in {timer.Elapsed.TotalMilliseconds:0.0} ms"));
                return page;
            });
        }

        internal bool IsCapturingCheatInput => PageFrame.Content is ControlsPage { IsCapturingInput: true };

        /// <summary>Persist the easter egg and refresh navigation without recreating the current page.</summary>
        internal void ApplyCheatAccess()
        {
            bool unlocked=CheatAccess.Session.Unlocked;
            var ws=Workspace.Current;
            ws.Settings.Set("cheats_unlocked",unlocked);
            if(!unlocked)
                CheatLaunchNotice.DisableAll((key,value)=>ws.Ini.Set(key,value),ws.Ini.Values.Select(pair=>pair.Key).ToArray());
            ws.SaveNow();
            string selected=_navigation.FirstOrDefault(pair=>Equals(pair.Value.Tag,"active")).Key??"home";
            bool leaveCheats=!unlocked && (PageFrame.Content is CheatsPage || selected=="cheats");
            if(leaveCheats)selected="tweaks";
            _navigation.Clear();SidebarItems.Children.Clear();SidebarItems.RowDefinitions.Clear();
            BuildSidebar();ScaleSidebar();SelectSidebar(selected);
            if(leaveCheats)NavigatePage(typeof(TweaksPage));
        }

        private readonly Dictionary<string, System.Windows.Controls.Button> _navigation = new();
        private void BuildSidebar()
        {
            foreach (var (key,label) in new[] {
                ("home","Start"),("play","Game"),("graphics","Graphics"),
                ("controls","Controls"),("tweaks","Tweaks"),("cheats","Cheats"),("online","Online"),
                ("trophies","Achievements"),("advanced","Settings"),("mods","More") })
            {
                if (key == "cheats" && !CheatAccess.Session.Unlocked) continue;
                var button=new System.Windows.Controls.Button { Content=BloodborneLauncher.Core.UiText.T(PcText.Get(label)),MinHeight=48,
                    VerticalAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center };
                if(key=="home"){button.FontSize=48;button.FontWeight=FontWeights.Bold;button.MinHeight=78;}
                SidebarItems.RowDefinitions.Add(new RowDefinition { Height=new GridLength(key=="home"?1.8:1,GridUnitType.Star) });
                Grid.SetRow(button,SidebarItems.RowDefinitions.Count-1);
                button.SetResourceReference(StyleProperty,"BloodborneSidebarButton");
                MenuTabSurface.SetShowUnderline(button,key!="home");
                MenuSounds.SetClickCue(button,MenuSoundCue.Select);
                button.Click+=(_,_)=>{if(key=="home")GoHome();else Show(key);};
                SidebarItems.Children.Add(button);_navigation[key]=button;
            }
        }
        private void SelectSidebar(string key)
        { foreach(var pair in _navigation)pair.Value.Tag=pair.Key==key?"active":null; }
        private void ScaleSidebar()
        {
            double scale=TabMetrics.SidebarScale(ActualWidth,ActualHeight);
            SidebarColumn.Width=new GridLength(220*scale);
            SidebarHost.Padding=new Thickness(0,26*scale,0,26*scale);
            SidebarItems.MinHeight=Math.Max(480,78+(_navigation.Count-1)*48)*scale;
            foreach(var pair in _navigation)
            {
                bool home=pair.Key=="home";
                pair.Value.FontSize=(home?48:26)*scale;
                pair.Value.MinHeight=(home?78:48)*scale;
                pair.Value.Padding=new Thickness(10*scale,0,10*scale,0);
            }
            // Keep enough room for the longest localized caption and soft glow wings.
            // Measure only on sidebar resize, never on hover/render.
            double span=0;
            double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach(var pair in _navigation)
            {
                if(pair.Key=="home")continue;
                var button=pair.Value;
                var caption=new FormattedText(button.Content?.ToString()??"",
                    System.Globalization.CultureInfo.CurrentUICulture,button.FlowDirection,
                    new Typeface(button.FontFamily,button.FontStyle,button.FontWeight,button.FontStretch),
                    button.FontSize,Brushes.White,dpi);
                span=Math.Max(span,caption.WidthIncludingTrailingWhitespace+16*scale);
            }
            SidebarColumn.Width=new GridLength(Math.Max(220*scale,span+20*scale));
            MenuTabSurface.SetUnderlineSpan(SidebarHost,span);
        }
        public bool IsMainMenu => PageFrame.Content is MenuHomePage;
        public void GoHome()
        {
            NavigatePage(typeof(MenuHomePage));
            SelectSidebar("home");
        }
        private void Home_Click(object sender, RoutedEventArgs e) => GoHome();

        private CancellationTokenSource? _navigationCancel;
        private long _navigationRequest;
        private bool _closed;
        private RetentionAudit? _retentionAudit;
        private readonly NavigationMemoryCoordinator _navigationMemory;
        private bool _navigationInProgress;

        // Saves do not induce GC. Nested More navigation shares the same bounded coordinator.
        public void QueuePageCleanup() => _retentionAudit?.Mark("settings");
        public void QueueNavigationCleanup()
        {
            _retentionAudit?.Mark("navigation");
            if(!_navigationInProgress)_navigationMemory.Queue();
        }
        private async void NavigatePage(Type type)
        {
            if(_closed)return;
            if(type == typeof(CheatsPage) && !CheatAccess.Session.Unlocked) type=typeof(TweaksPage);
            _navigationInProgress=true;_navigationMemory.Suspend();
            long request=++_navigationRequest;
            _navigationCancel?.Cancel();_navigationCancel?.Dispose();
            _navigationCancel=new CancellationTokenSource();
            var cancel=_navigationCancel.Token;
            // The book follows opacity only when crossing the Start boundary.
            // It stays at its base opacity throughout settings-to-settings fades.
            BookHost.FollowPageFade=type==typeof(MenuHomePage) || PageFrame.Content is null or MenuHomePage;
            if(PageFrame.Content is LauncherPage previous && previous.GetType()!=type)
            {
                try { await MenuTransition.LeaveAsync(PageFrame,cancel); }
                catch(OperationCanceledException) { return; }
                if(_closed || request!=_navigationRequest)return;
            }
            else MenuTransition.Reset(PageFrame);
            _shownRefresh?.Abort(); _shownRefresh = null;
            var page = GetPage(type);
            BackdropShade.Fill = type == typeof(MenuHomePage) ? HomeShade : PageShade;
            SettingsShade.Visibility = type == typeof(MenuHomePage) ? Visibility.Collapsed : Visibility.Visible;
            BookHost.Active = type != typeof(MenuHomePage);
            if (!ReferenceEquals(PageFrame.Content,page))
            {
                PageFrame.Opacity = 0;
                PageFrame.Content=BloodborneLauncher.Core.UiText.T(page);
                using (LauncherDiagnostics.Measure("Refresh before transition " + page.GetType().Name)) page.OnShown();
                // Expand templates and settle responsive columns before animation clocks begin.
                PageFrame.UpdateLayout();
                MenuTransition.Enter(PageFrame,false,()=>{
                    if(_closed || request!=_navigationRequest)return;
                    if(page is not MenuHomePage)BookHost.FollowPageFade=false;
                    _navigationInProgress=false;QueueNavigationCleanup();
                });
            }
            else { _navigationInProgress=false;RefreshAfterNavigation(page);QueueNavigationCleanup(); }
        }

        /// <summary>Preserve links from existing setting pages and diagnostics.</summary>
        public void Show(string page)
        {
            if (page == "cheats" && !CheatAccess.Session.Unlocked) page = "tweaks";
            var type = page switch {
                "controls" => typeof(ControlsPage), "graphics" => typeof(GraphicsPage),
                "display" => typeof(GraphicsPage), "trophies" => typeof(TrophiesPage),
                "online" => typeof(OnlinePage), "tweaks" => typeof(TweaksPage), "cheats" => typeof(CheatsPage),
                "advanced" => typeof(SettingsPage), "mods" or "output" => typeof(MorePage),
                _ => typeof(PlayPage),
            };
            NavigatePage(type);
            SelectSidebar(page == "output" ? "mods" : page == "display" ? "graphics" : page);
            if (type == typeof(MorePage)) ((MorePage)GetPage(type)).ShowSection(page);
        }

        // ---------------------------------------------------------------- play

        private async void TitlePlay_Click(object sender, RoutedEventArgs e) => await PlayOrStopAsync();

// The upstream update/install entry points are deliberately excluded from the build.
#if false // Intentionally disabled; retained as upstream reference.
        private async void TitleUpdate_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync();

        /// <summary>Asks, downloads the newer launcher, swaps it in and restarts.</summary>
        public async Task InstallUpdateAsync()
        {
            var ws = Workspace.Current;
            if (ws.Update is not { } release) return;
            await InstallAsync(release, await Updater.NeedsPackageAsync(release));
        }

        /// <summary>An older package's engine: download the latest package and update it in place.</summary>
        public async Task UpdateEngineAsync()
        {
            var ws = Workspace.Current;
            var release = ws.Latest ?? await Updater.LatestAsync(Updater.BetaChannel(ws.Settings));
            if (release?.ZipUrl == null)
            {
                Prompt.Tell("Update engine", $"No package is published yet. See {Updater.ReleasesPage}");
                return;
            }
            await InstallAsync(release, package: true);
        }

        /// <summary>A release the player picked in Settings › Updates › Version, older ones included.</summary>
        public async Task InstallVersionAsync(Updater.Release release)
        {
            if (release.IsCurrent)
            {
                Prompt.Tell("Already installed", $"You already have {Updater.CurrentLabel}.");
                return;
            }
            await InstallAsync(release, await Updater.SwitchNeedsPackageAsync(release), chosen: true);
        }

        private async Task InstallAsync(Updater.Release release, bool package, bool chosen = false)
        {
            var ws = Workspace.Current;
            if (package && ws.Game.Running)
            {
                Prompt.Tell("Close the game first", "This update replaces the engine, so the game has to be closed.");
                return;
            }
            var notes = release.Notes.Trim();
            if (notes.Length > 1200) notes = notes[..1200] + "…";
            var size = package ? release.ZipSize : release.ExeSize;
            var older = !release.IsNewerThan(Updater.Current, Updater.CurrentBeta);
            var body = $"You have {Updater.CurrentLabel}. " +
                       (package
                           ? $"{(chosen ? "This version has a different engine" : "This update includes a new engine")}, so the whole package is downloaded ({size / 1048576} MB) and replaced in place."
                           : $"The launcher downloads {release.Tag} ({size / 1048576} MB), replaces itself and restarts.") +
                       " Your settings, saves, mods and profiles stay." +
                       (chosen && older
                           ? "\n\nThis is an older version: settings it doesn't know are kept but not used, and the launcher may offer the newest version again at start."
                           : "") +
                       (notes.Length > 0 ? $"\n\n{(chosen && older ? "This version's notes" : "What's new")}:\n" + notes : "");
            var verb = chosen ? "Switch" : "Update";
            if (!Prompt.Confirm($"{verb} to {release.Name}?", body, $"{verb} now"))
                return;

            TitleUpdate.Visibility = Visibility.Visible;
            TitleUpdate.IsEnabled = false;
            var progress = new Progress<double>(p => TitleUpdateText.Text = $"Downloading {p:P0}");
            try
            {
                ws.SaveNow();
                var skipped = await Updater.InstallAsync(release, package, progress);
                if (skipped.Count > 0)
                    Prompt.Tell("Updated, with files left out",
                        "Windows or an antivirus would not let these through, so the old ones were kept:\n" +
                        string.Join("\n", skipped.Take(5)) +
                        "\n\nThe game runs without them; only online play with The Hunter's Dream (bbbridge) needs " +
                        $"them. Allow the Bloodborne PC folder in the antivirus and update again, or take them from {Updater.ReleasesPage}");
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                TitleUpdate.IsEnabled = true;
                TitleUpdateText.Text = $"Update {release.Tag}";
                ws.Log("Update failed: " + ex.GetBaseException().Message, LogLevel.Bad);
                Prompt.Tell("Update failed", ex.GetBaseException().Message + $"\n\nYou can download it by hand from {Updater.ReleasesPage}");
            }
        }

#endif

        /// <summary>The one place the game is started from, so the title bar and the Play page agree.</summary>
        public async Task PlayOrStopAsync()
        {
            if (RenderAudit.Enabled) return;
            var ws = Workspace.Current;
            if (ws.Game.Running)
            {
                await ws.StopGameAsync();
                return;
            }

            var s = ws.Settings;
            var ini = ws.Ini;
            // A connected Hunter's Dream account plays in its own co-op pool (the engine's thd backend);
            // decided here, not only when the Online page is opened.
            ini.Set("online_pool", s.HuntersDreamToken.Length > 0 && s.Str("hd_name").Length > 0 ? "huntersdream" : "");
            if (!HuntersDream.OnlinePaused && ini.Get("online_enabled") == "1" && ini.Get("online_pool") != "huntersdream")
            {
                // The engine refuses these at start; say so here instead.
                var problem = ini.Get("online_username").Contains('@') ? "The account name is the name registered on the server, not an email address."
                    : ini.Get("online_username").Trim().Length == 0 ? "Enter the account name before playing online."
                    : s.OnlinePassword.Length == 0 ? "Enter the account password before playing online."
                    : null;
                if (problem != null)
                {
                    Prompt.Tell(BloodborneLauncher.Core.UiText.T("Online"), BloodborneLauncher.Core.UiText.Format("{0}\n\nOr turn \u0022Play online\u0022 off to play offline.",problem));
                    Show("online");
                    return;
                }
            }

            // No port folder yet: look for it again, then let the player point at it right here.
            if (!GameRunner.Custom(s) && !Paths.IsPortDir(Paths.PortDir))
            {
                ws.ResolvePort();
                if (!Paths.IsPortDir(Paths.PortDir))
                {
                    if (!Prompt.Confirm(BloodborneLauncher.Core.UiText.T("Where is the engine?"),
                            "The launcher needs the Bloodborne PC engine (the folder with run.py and bin\\bb-probe.exe). " +
                            "It is normally the folder the launcher came in.", BloodborneLauncher.Core.UiText.T("Choose folder")))
                        return;
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = BloodborneLauncher.Core.UiText.T("Port folder (with Bloodborne.exe and run.py)") };
                    if (dialog.ShowDialog(this) != true) return;
                    if (!Paths.IsPortDir(dialog.FolderName))
                    {
                        Prompt.Tell(BloodborneLauncher.Core.UiText.T("Not the port folder"), UiText.T("That folder has no run.py with Bloodborne.exe or bin\\bb-probe.exe."));
                        return;
                    }
                    s.Set("port_dir", dialog.FolderName);
                    ws.ResolvePort();
                    ws.SaveNow();
                }
            }

            // A patch that changes the game's region (Unlock Game Region) leaves the game without a
            // Bloodborne server: online, ask whether to leave it out for this session.
            GameEnvironment.RegionPatchOnline = null;
            if (!HuntersDream.OnlinePaused && ini.Get("online_enabled") == "1" && ini.Get("online_connected", "1") == "1" &&
                Patches.RegionChanging(GameEnvironment.PatchesDir(s)) is { Count: > 0 } region)
            {
                var names = string.Join(", ", region.Select(p => $"\"{p.Name}\""));
                var choice = Prompt.Choose(BloodborneLauncher.Core.UiText.Format("Play online with {0}?",names),
                    BloodborneLauncher.Core.UiText.Format("{0} moves the game to a region that has no Bloodborne server, so Play online fails with \u0022Failure to acquire information from the Bloodborne server\u0022.\n\nLeave it out for this session? It stays on for offline play.",names),
                    BloodborneLauncher.Core.UiText.T("Cancel"), BloodborneLauncher.Core.UiText.T("Keep it on"), "Leave it out");
                if (choice <= 0) return;
                GameEnvironment.RegionPatchOnline = choice == 1 ? "keep" : "skip";
                ws.Log(choice == 1 ? BloodborneLauncher.Core.UiText.F($"{names}: kept on while online; Play online will not reach the server.")
                    : BloodborneLauncher.Core.UiText.F($"{names}: left out while online; it applies again with online off."), LogLevel.Step);
            }

            // A third-party resolution patch while the output resolution sets the game's resolution
            // and UI itself: it would keep its own shape (black bars) and stop the widescreen hooks.
            GameEnvironment.ResolutionPatchChoice = null;
            var (outW, outH) = Choices.OutputSize(ini.Get("output_res", "1920x1080"));
            // With a 1920 × 1080 output, such a patch makes the game draw at its size itself: the
            // upscaler then runs at 1:1 (DLAA whatever the preset) and in-game preset changes crashed
            // (Discord, 3440 × 1440). Its size as the output resolution keeps the shape and lets the
            // presets work: offered once per set of patches.
            if ((outW, outH) == (1920, 1080) &&
                Patches.ResolutionSetting(GameEnvironment.PatchesDir(s)) is { Count: > 0 } native &&
                native.Select(p => System.Text.RegularExpressions.Regex.Match(p.Name, @"(\d{3,5})x(\d{3,5})"))
                    .FirstOrDefault(m => m.Success) is { } size &&
                (int.Parse(size.Groups[1].Value), int.Parse(size.Groups[2].Value)) is var (patchW, patchH) &&
                (patchW, patchH) != (1920, 1080))
            {
                var names = string.Join(", ", native.Select(p => $"\"{p.Name}\""));
                if (s.Str("resolution_patch_native") != names)
                {
                    var choice = Prompt.Choose(BloodborneLauncher.Core.UiText.F($"Use {patchW} × {patchH} as the output resolution?"),
                        BloodborneLauncher.Core.UiText.Format("{0}{1}the render resolution (the upscaler runs as DLAA / native AA).\n\n{2}and the presets (Quality, Balanced, Performance) work. The patch is then left out while that output is set.",names,BloodborneLauncher.Core.UiText.F($" makes the game draw at {patchW} × {patchH} by itself, so the upscaler preset can't lower "),BloodborneLauncher.Core.UiText.F($"Setting the output resolution to {patchW} × {patchH} instead gives the same picture and shape, ")),
                        BloodborneLauncher.Core.UiText.T("Cancel"), BloodborneLauncher.Core.UiText.F($"Use {patchW} × {patchH}"), "Keep the patch");
                    if (choice <= 0) return;
                    if (choice == 1)
                    {
                        ini.Set("output_res", $"{patchW}x{patchH}");
                        ini.Save();
                        (outW, outH) = (patchW, patchH);
                        GameEnvironment.ResolutionPatchChoice = "skip";
                        ws.Log(BloodborneLauncher.Core.UiText.F($"Output resolution {patchW} × {patchH}; {names} left out (presets work)."), LogLevel.Step);
                    }
                    else
                    {
                        s.Set("resolution_patch_native", names);
                        ws.SaveNow();
                    }
                }
            }
            if ((outW, outH) != (1920, 1080) && GameEnvironment.ResolutionPatchChoice is null &&
                Patches.ResolutionSetting(GameEnvironment.PatchesDir(s)) is { Count: > 0 } sizing)
            {
                var names = string.Join(", ", sizing.Select(p => $"\"{p.Name}\""));
                // "Always on" is remembered for this set of patches (#18); another set asks again.
                var choice = s.Str("resolution_patch_always") == names ? 1 : Prompt.Choose(BloodborneLauncher.Core.UiText.Format("Play with {0}?",names),
                    BloodborneLauncher.Core.UiText.Format("{0}{1} sets its own on top: the picture keeps the patch\u0027s shape, with black bars.\n\nLeave it out? It still applies with a 1920 \u00D7 1080 output. \u0022Always on\u0022 keeps it and does not ask again until the resolution patches switched on in Mods change.",BloodborneLauncher.Core.UiText.F($"The output resolution ({outW} × {outH}) already sets the game's resolution and widens the picture and UI to fill the screen. "),names),
                    BloodborneLauncher.Core.UiText.T("Cancel"), BloodborneLauncher.Core.UiText.T("Keep on"), "Leave out", "Always on");
                if (choice <= 0) return;
                if (choice == 3)
                {
                    s.Set("resolution_patch_always", names);
                    ws.SaveNow();
                    choice = 1;
                }
                GameEnvironment.ResolutionPatchChoice = choice == 1 ? "keep" : "skip";
                ws.Log(choice == 1 ? BloodborneLauncher.Core.UiText.F($"{names}: kept on with the {outW} × {outH} output.")
                    : BloodborneLauncher.Core.UiText.F($"{names}: left out; the output resolution sets the picture."), LogLevel.Step);
            }

            // A game left running in the background (an earlier start that never ended, or another
            // launcher's) keeps its whole memory pool, and a new start fails (Windows error 1450).
            var leftovers = System.Diagnostics.Process.GetProcessesByName("bb-probe");
            if (leftovers.Length > 0)
            {
                var choice = Prompt.Choose(BloodborneLauncher.Core.UiText.T("The game is still running in the background"),
                    BloodborneLauncher.Core.UiText.Format("bb-probe.exe from an earlier start is still running and holds the game\u0027s memory, so a new start would fail. Close it and start?"), BloodborneLauncher.Core.UiText.T("Cancel"), BloodborneLauncher.Core.UiText.T("Close it and start"));
                if (choice <= 0) return;
                foreach (var leftover in leftovers)
                {
                    try
                    {
                        leftover.Kill(entireProcessTree: true);
                        leftover.WaitForExit(5000);
                    }
                    catch { /* already gone */ }
                    leftover.Dispose();
                }
                ws.Log(BloodborneLauncher.Core.UiText.T("Closed the game left running in the background."), LogLevel.Step);
            }

            if (!s.Bool("memory_check_off") && MemoryCheck.Problem(ws.Ini) is { } memory)
            {
                var choice = Prompt.Choose(BloodborneLauncher.Core.UiText.T("Windows may run out of memory"), BloodborneLauncher.Core.UiText.T(memory), BloodborneLauncher.Core.UiText.T("Cancel"), BloodborneLauncher.Core.UiText.T("Don't ask again"), "Start anyway");
                if (choice <= 0) return;
                if (choice == 1) s.Set("memory_check_off", true);
            }

            var error = ws.StartGame();
            if (error != null)
            {
                System.Windows.MessageBox.Show(this, error, UiText.T("Could not start the game"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (s.Bool("switch_to_output"))
                Show("output");
        }

        private void UpdatePlayButton()
        {
            if (_pages.TryGetValue(typeof(MenuHomePage), out var home))
                ((MenuHomePage)home).SetRunning(Workspace.Current.Game.Running);
            var running = Workspace.Current.Game.Running;
            foreach (var page in _pages.Values)
                (page as MenuHomePage)?.UpdateSessionStatus();
        }

    }
}
