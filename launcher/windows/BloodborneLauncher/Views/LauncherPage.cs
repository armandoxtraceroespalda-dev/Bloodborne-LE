using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BloodborneLauncher.Core;
using BloodborneLauncher.Controls;
using Button = BloodborneLauncher.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = BloodborneLauncher.Controls.TextBox;

namespace BloodborneLauncher.Views
{
    /// <summary>
    /// One setting: its title on the left (descriptions in an ⓘ tooltip), its control on the right,
    /// or below it for wide controls such as segmented buttons. Hint is a line under the title for
    /// live status (a path, "not available on this GPU"); it stays hidden until something is set.
    /// </summary>
    public sealed class SettingRow : Border
    {
        private SymbolIcon? _icon;
        private readonly SymbolRegular _symbol;
        private readonly Grid _iconGrid;
        public SymbolIcon Icon
        {
            get
            {
                if (_icon != null) return _icon;
                // Most Bloodborne rows hide icons; allocate a glyph control only when requested.
                _icon = new SymbolIcon { Symbol = _symbol, FontSize = Classic ? 20 : 18,
                    Margin = new Thickness(0, 0, Classic ? 16 : 12, 0), VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed };
                _iconGrid.Children.Add(_icon);
                return _icon;
            }
        }
        public TextBlock Hint { get; }

        public bool ShowIcon
        {
            get => _icon?.Visibility == Visibility.Visible;
            set
            {
                if (value) Icon.Visibility = Visibility.Visible;
                else if (_icon != null) _icon.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Classic layout: one card per setting with its icon and description, as before 0.9.4.</summary>
        public static bool Classic => true;

        private readonly TextBlock _name;
        private readonly StackPanel _text;
        private readonly Grid _grid;
        private readonly UIElement? _control;
        private readonly bool _wide;
        private readonly string? _info;
        private TextBlock? _description;
        private string? _note;
        private string? _lastHelp;
        public bool NoteInTooltip { get; set; }
        public bool TightLabel { get; set; }
        public void StyleNameAsHeading()
        {
            _name.SetResourceReference(StyleProperty, "GroupHeader");
            _name.FontSize = 21.875;
            _name.FontFamily = LauncherAppearance.MenuFont;
            _name.SetResourceReference(TextBlock.ForegroundProperty, "SectionHeadingBrush");
            _name.Margin = new Thickness(0);
        }

        public void HideName() => _name.Visibility = Visibility.Collapsed;

        public SettingRow(SymbolRegular icon, string title, string? info, object? control, bool wide)
        {
            _symbol = icon; _control = control as UIElement; _wide = wide; _info = UiText.T(info);
            Margin = new Thickness(0, 0, 0, 2);
            Background = Brushes.Transparent;
            _name = new TextBlock { Text = UiText.T(title), FontSize = 19.25,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Hint = new TextBlock { FontSize = 14, FontFamily = LauncherAppearance.DetailFont,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
            Hint.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            _text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _text.Children.Add(_name);
            _grid = new Grid(); _iconGrid = _grid;
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.ColumnDefinitions.Add(new ColumnDefinition());
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_text, 1); _grid.Children.Add(_text);
            if (_control != null) _grid.Children.Add(_control);
            Grid.SetColumn(Hint, 1); Grid.SetColumnSpan(Hint, 2); Grid.SetRow(Hint, 2); _grid.Children.Add(Hint);
            var surface = new Grid(); surface.Children.Add(_grid);
            var line = new System.Windows.Shapes.Rectangle { Height = 1, VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false, Opacity = 0.65 };
            line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "CardStrokeColorDefaultBrush");
            surface.Children.Add(line); Child = surface;
            if (control is Control item) item.FontFamily = item is System.Windows.Controls.TextBox
                ? LauncherAppearance.DetailFont : LauncherAppearance.MenuFont;
            SizeChanged += (_, _) => UpdateLayoutMode();
            UpdateLayoutMode();
        }

        public void SetNote(string? note)
        {
            _note = note;
            UpdateHelp();
            Hint.Text = note ?? "";
            Hint.Visibility = MenuHelp.GetCompact(this) && NoteInTooltip || string.IsNullOrEmpty(note)
                ? Visibility.Collapsed : Visibility.Visible;
        }
        private void UpdateHelp()
        {
            string text = string.Join("\n\n", new[] { _info, _note }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct());
            if (text == _lastHelp) return;
            _lastHelp = text; MenuHelp.Attach(this, string.IsNullOrEmpty(text) ? null : text);
        }
        internal void UpdateLayoutMode()
        {
            if (_grid == null) return;
            bool compact = MenuHelp.GetCompact(this);
            _name.TextWrapping = compact ? TextWrapping.Wrap : TextWrapping.NoWrap;
            _name.TextTrimming = compact ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            Hint.SetResourceReference(TextBlock.ForegroundProperty, compact ? "WarningTextBrush" : "TextFillColorTertiaryBrush");
            MinHeight = compact ? 50 : 70;
            Padding = compact ? new Thickness(10, 7, 10, 9) : new Thickness(18, 12, 18, 16);
            if (!compact && !string.IsNullOrWhiteSpace(_info) && _description == null)
            {
                _description = new TextBlock { Text = _info, FontSize = 14, FontFamily = LauncherAppearance.DetailFont,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                _description.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                _text.Children.Add(_description);
            }
            if (_description != null) _description.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            double space = Math.Max(100, ActualWidth - Padding.Left - Padding.Right);
            double gap = TightLabel ? 0 : Math.Clamp(space * 0.02, 6, 16);
            _name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelMinimum = TightLabel ? Math.Max(1, _name.DesiredSize.Width + 2) : Math.Clamp(_name.DesiredSize.Width, 90, 260);
            double controlMinimum = _control switch
            {
                ToggleSwitch => 60,
                System.Windows.Controls.Primitives.ToggleButton => 60,
                FrameworkElement { Width: <= 100 } small => Math.Max(40, small.Width),
                Grid => 210,
                _ => 150
            };
            bool slider = _control is FrameworkElement { Tag: "slider-value-track" };
            bool toggle = _control is ToggleSwitch or System.Windows.Controls.Primitives.ToggleButton;
            bool below = _wide || slider || compact && !toggle && _control != null && space < labelMinimum + gap + controlMinimum;
            _grid.ColumnDefinitions[1].Width = !compact || below ? new GridLength(1, GridUnitType.Star)
                : toggle ? new GridLength(1, GridUnitType.Star)
                : new GridLength(TightLabel ? labelMinimum : Math.Min(320, Math.Max(labelMinimum, space * 0.36)));
            _grid.ColumnDefinitions[2].Width = compact && !below && !toggle ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            if (_control != null)
            {
                Grid.SetRow(_control, below ? 1 : 0);
                Grid.SetColumn(_control, below ? 1 : 2);
                Grid.SetColumnSpan(_control, below ? 2 : 1);
                if (_control is FrameworkElement fe)
                {
                    fe.Margin = slider ? new Thickness(0, 6, 0, 3) : below ? new Thickness(0, 5, 0, 0) : new Thickness(gap, 0, 0, 0);
                    fe.HorizontalAlignment = slider ? HorizontalAlignment.Center : toggle ? HorizontalAlignment.Right
                        : below ? HorizontalAlignment.Stretch : compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                    Fit(fe, below ? space : toggle ? Math.Min(80, space) : Math.Max(1, space - _grid.ColumnDefinitions[1].Width.Value - gap));
                }
            }
            UpdateHelp();
            if (NoteInTooltip) Hint.Visibility = compact || string.IsNullOrEmpty(Hint.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>A picture in the icon's place (a trophy's own), <paramref name="size"/> pixels
        /// square; grey when <paramref name="grey"/>.</summary>
        public void SetPicture(string file, double size, bool grey)
        {
            if (Icon.Parent is not Grid grid || Picture(file, (int)(size * 2), grey) is not { } source) return;
            var image = new System.Windows.Controls.Image
            {
                Source = source, Width = size, Height = size, Margin = Icon.Margin, VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            grid.Children.Add(image);
            Icon.Visibility = Visibility.Collapsed;
        }

        /// <summary>A picture file decoded at <paramref name="pixels"/> wide, frozen; null if it cannot be read.</summary>
        public static ImageSource? Picture(string file, int pixels, bool grey = false)
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = pixels;
                bitmap.UriSource = new Uri(file);
                bitmap.EndInit();
                bitmap.Freeze();
                if (!grey) return bitmap;
                var gray = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
                gray.Freeze();
                return gray;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Fit(FrameworkElement element, double available)
        {
            element.MaxWidth = Math.Max(1, available);
            if (element is StackPanel { Orientation: Orientation.Horizontal } row)
            {
                double fixedWidth = row.Children.OfType<FrameworkElement>().Where(c => c is TextBlock || c is BloodborneLauncher.Controls.Button)
                    .Sum(c => (double.IsNaN(c.Width) ? 34 : c.Width) + c.Margin.Left + c.Margin.Right);
                foreach (var child in row.Children.OfType<FrameworkElement>().Where(c => c is not TextBlock && c is not BloodborneLauncher.Controls.Button))
                    child.MaxWidth = Math.Max(60, available - fixedWidth - 12);
            }
        }

    }

    /// <summary>Native underline choices with mouse and keyboard activation.</summary>
    public sealed class Segmented : Border
    {
        private readonly List<System.Windows.Controls.Button> _items=new();
        public int SelectedIndex { get; private set; }=-1;
        public event Action<int>? Changed;
        public Segmented(IReadOnlyList<Choice> choices,Func<Choice,bool>? enabled=null,double spacing=4,bool uniform=false,double fontSize=15.75,double minHeight=42)
        {
            Background=Brushes.Transparent;
            var grid=new Grid { HorizontalAlignment=uniform?HorizontalAlignment.Stretch:HorizontalAlignment.Center };
            for(int i=0;i<choices.Count;i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width=uniform?new GridLength(1,GridUnitType.Star):GridLength.Auto });int index=i;var choice=choices[i];
                var item=new System.Windows.Controls.Button { Content=Short(choice.Label),FontSize=fontSize,MinHeight=minHeight,Margin=new Thickness(spacing,0,spacing,0),
                    HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,
                    ToolTip=choice.Note??choice.Label,IsEnabled=enabled?.Invoke(choice)??true };
                item.SetResourceReference(StyleProperty,"BloodborneSegmentButton");
                item.Click+=(_,_)=>Select(index,true);Grid.SetColumn(item,i);grid.Children.Add(item);_items.Add(item);
            }
            Child=grid;
            PreviewKeyDown+=(_,e)=>{
                if(e.Key is not (Key.Left or Key.Right))return;
                int direction=e.Key==Key.Right?1:-1;
                for(int n=1;n<=_items.Count;n++)
                {
                    int i=(System.Math.Max(0,SelectedIndex)+direction*n+_items.Count*2)%_items.Count;
                    if(!_items[i].IsEnabled)continue;Select(i,true);_items[i].Focus();e.Handled=true;break;
                }
            };
        }
        public static string Short(string label)=>label.Split(" (")[0];
        public void Select(int index,bool notify)
        {
            if(index<0 || index>=_items.Count || !_items[index].IsEnabled)return;
            SelectedIndex=index;
            for(int i=0;i<_items.Count;i++)_items[i].Tag=i==index?"active":null;
            if(notify)Changed?.Invoke(index);
        }
    }

    /// <summary>
    /// A settings page: groups of compact rows, each group one rounded box (Sectionize puts the
    /// rows of a group into it once the page is built). The helpers bind a control straight to a
    /// settings.json or bbport.ini key and save on change.
    /// </summary>
    public abstract class LauncherPage : UserControl, IDisposable
    {
        protected static Workspace Ws => Workspace.Current;
        protected static LauncherSettings S => Ws.Settings;
        protected static IniFile Ini => Ws.Ini;

        protected StackPanel Body { get; }
        protected ScrollViewer Scroller { get; }
        private TranslateTransform? _bookScrollTransform;
        internal void MoveBookScrollBar(double offset)
        {
            if(Scroller==null)return;
            Scroller.ClipToBounds=false; // Content presenter still clips text; the translated bar may reach the paper.
            Scroller.ApplyTemplate();
            if(Scroller.Template?.FindName("PART_VerticalScrollBar",Scroller) is not System.Windows.Controls.Primitives.ScrollBar bar)return;
            // Keep the full track length and raise it above the mute button.
            // The mute footprint is 32 + 10 DIPs; the page already reserves 8.
            // Moving up 38 leaves a further 4-DIP gap without changing layout.
            bar.Margin=new Thickness(0);
            bar.Height=double.NaN;
            bar.VerticalAlignment=VerticalAlignment.Stretch;
            _bookScrollTransform??=new TranslateTransform();
            _bookScrollTransform.Y=-38;
            if(_bookScrollTransform.X!=offset)_bookScrollTransform.X=offset;
            if(!ReferenceEquals(bar.RenderTransform,_bookScrollTransform))bar.RenderTransform=_bookScrollTransform;
        }

        private bool _sectioned;
        private readonly bool _settingsShell;
        private readonly PageLifetime _lifetime = new();
        private bool _disposed;
        protected bool IsDisposed => _disposed;
        protected void ReleaseWhenDisposed(Action release) => _lifetime.Add(release);
        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true;
            try { _lifetime.Dispose(); }
            finally
            {
                Content=null;
                Body?.Children.Clear();
                if(Scroller!=null)Scroller.Content=null;
            }
        }

        protected LauncherPage(string title, string subtitle, bool settingsShell=true)
        {
            _settingsShell=settingsShell;
            MenuHelp.SetCompact(this, this is not PlayPage);
            var lease=ViewRetention.Register(this);
            ReleaseWhenDisposed(lease.Retire);
            Background = Brushes.Transparent;
            HorizontalContentAlignment=HorizontalAlignment.Stretch;
            VerticalContentAlignment=VerticalAlignment.Stretch;
            if (LauncherAppearance.Bloodborne) FontFamily = LauncherAppearance.MenuFont;
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");

            FontSize=18.375;
            if(!settingsShell) { Body=null!;Scroller=null!;return; }
            PageTypography.Attach(this);

            // The tab says which page this is; no page title.
            Body = new StackPanel { Margin = new Thickness(4, 4, 4, 8) };
            // Suppress section backgrounds, not dialogs attached outside Body.
            MenuSurface.SetSuppressSectionShadow(Body,true);
            Scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = BloodborneLauncher.Core.UiText.T(Body) };
            var shell = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition());
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new Grid { MinHeight = 32, MaxWidth = 1600 };
            var label = new TextBlock { Text = BloodborneLauncher.Core.UiText.T(title), FontSize = 21.875, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0),
                // Lower the caption without resizing the heading slot or scrollbar.
                RenderTransform = new TranslateTransform(0,6) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SectionHeadingBrush");
            heading.Children.Add(label);shell.Children.Add(heading);
            Grid.SetRow(Scroller, 1);shell.Children.Add(Scroller);
            Content = BloodborneLauncher.Core.UiText.T(shell);
            PageScroll.Attach(Scroller);
            Loaded += (_, _) =>
            {
                PrepareForDisplay();
            };
        }

        /// <summary>Build the final section tree before WPF expands templates in a visible host.</summary>
        public void PrepareForDisplay()
        {
            if (_sectioned || _disposed) return;
            _sectioned = true;
            if(_settingsShell)Sectionize();
        }

        /// <summary>Layer a dialog over the entire settings shell without reparenting its scroller.</summary>
        protected void AttachOverlay(UIElement overlay)
        {
            if (Content is not UIElement settings)
                throw new InvalidOperationException(BloodborneLauncher.Core.UiText.T("The settings shell must exist before adding an overlay."));
            var root = new Grid();
            Content = null;
            root.Children.Add(settings);
            root.Children.Add(overlay);
            Content = BloodborneLauncher.Core.UiText.T(root);
        }

        /// <summary>Called each time the page is shown; refresh anything read from disk.</summary>
        public virtual void OnShown() { }

        /// <summary>
        /// Puts the rows that follow each group header into one rounded box. Rows, panels of rows
        /// and short captions go in; anything else (an info bar, a table, a summary card) ends the box.
        /// </summary>
        private void Sectionize()
        {
            var children = Body.Children.Cast<UIElement>().ToList(); Body.Children.Clear();
            if (this is PlayPage)
            {
                StackPanel? box = null;
                foreach (var child in children)
                {
                    if (child is TextBlock { Tag: "group" }) { Body.Children.Add(child); box = null; continue; }
                    if (child is MenuSurface) { Body.Children.Add(child); box = null; continue; }
                    if (box == null) { box = new StackPanel(); Body.Children.Add(new MenuSurface { Child = box,
                        Padding = new Thickness(16,14,16,16), Margin = new Thickness(-8,0,-8,10) }); }
                    box.Children.Add(child);
                }
                return;
            }
            var columns = new SettingsColumns();
            SettingsSection? section = null; StackPanel? rows = null;
            bool hasHeading = false;
            void Begin(UIElement? heading = null)
            {
                hasHeading = heading != null;
                rows = new OptionRows();
                section = new SettingsSection(heading, new MenuSurface { Child = rows, Padding = new Thickness(10,8,10,12) })
                    { Margin = new Thickness(0,0,0,16) };
                columns.Children.Add(section);
                if (this is ModsPage or OutputPage or TrophiesPage) SettingsColumns.SetFullWidth(section, true);
                if (heading != null && SettingsColumns.GetFullWidth(heading)) SettingsColumns.SetFullWidth(section, true);
                if (heading != null && SettingsColumns.GetTwoColumns(heading)) SettingsColumns.SetTwoColumns(section, true);
            }
            foreach (var child in children)
            {
                if (child is FrameworkElement { Tag: "standalone" })
                {
                    columns.Children.Add(child); SettingsColumns.SetFullWidth(child, true);
                    section = null; rows = null; hasHeading = false; continue;
                }
                if (child is FrameworkElement { Tag: "group" }) { Begin(child); continue; }
                if (child is StackPanel panel && panel.Children.OfType<TextBlock>().FirstOrDefault()?.Tag as string == "group")
                {
                    var title = panel.Children.OfType<TextBlock>().First();
                    panel.Children.Remove(title);
                    Begin(title);
                    section!.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = panel });
                    rows!.Children.Add(child); continue;
                }
                if (section == null) Begin();
                if (hasHeading && child is TextBlock caption && !string.IsNullOrWhiteSpace(caption.Text))
                {
                    if (caption.Tag as string != "no-tooltip") MenuHelp.Attach(section!, caption.Text);
                    // Instructions remain discoverable from the section, without permanent prose.
                    caption.Visibility = Visibility.Collapsed;
                }
                rows!.Children.Add(child);
                if (SettingsColumns.GetFullWidth(child)) SettingsColumns.SetFullWidth(section!, true);
            }
            Body.Children.Add(columns);
        }

        // ---------------------------------------------------------------- building blocks

        public static TextBlock Text(string text, string style)
        {
            var block = new TextBlock { Text = BloodborneLauncher.Core.UiText.T(text) };
            block.SetResourceReference(StyleProperty, style);
            return block;
        }

        protected TextBlock Group(string text, Panel? into = null)
        {
            var header = Text(BloodborneLauncher.Core.UiText.T(text), "GroupHeader");
            header.Tag = "group";
            (into ?? Body).Children.Add(header);
            return header;
        }

        public static SymbolIcon Icon(SymbolRegular symbol) => new() { Symbol = symbol };

        /// <summary>A setting row. info goes in the ⓘ tooltip; hintBlock is a visible status line, empty until set.</summary>
        public static SettingRow Card(SymbolRegular icon, string title, string? info, object? control, out TextBlock hintBlock, bool wide = false)
        {
            var row = new SettingRow(icon, title, info, control, wide);
            hintBlock = row.Hint;
            return row;
        }

        public static void SetHint(TextBlock hint, string? text)
        {
            if (hint.Tag is SettingRow owner) { owner.SetNote(UiText.T(text)); return; }
            hint.Text = BloodborneLauncher.Core.UiText.T(text);
            hint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        protected SettingRow Add(SettingRow row, Panel? into = null)
        {
            (into ?? Body).Children.Add(row);
            return row;
        }

        public static Button SmallButton(string text, SymbolRegular icon, RoutedEventHandler click, ControlAppearance look = ControlAppearance.Secondary)
        {
            var button = new Button
            {
                Content = BloodborneLauncher.Core.UiText.T(text),
                Icon = Icon(icon),
                Appearance = look,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.Click += click;
            return button;
        }

        public static Button IconButton(SymbolRegular icon, string tooltip, RoutedEventHandler click)
        {
            var button = new Button
            {
                Icon = Icon(icon),
                ToolTip = BloodborneLauncher.Core.UiText.T(tooltip),
                Appearance = ControlAppearance.Transparent,
                Padding = new Thickness(6),
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.Click += click;
            return button;
        }

        public static StackPanel Row(params UIElement[] children)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var child in children)
                panel.Children.Add(child);
            return panel;
        }

        // ---------------------------------------------------------------- bound rows

        /// <summary>A choice: segmented buttons for a few short options, a drop-down otherwise.
        /// The chosen option's note shows under the row.</summary>
        protected FrameworkElement ComboRow(SymbolRegular icon, string title, string? hint, IReadOnlyList<Choice> choices,
            Func<string> get, Action<string> set, Panel? into = null, double width = 240, Action? changed = null,
            Func<Choice, bool>? enabled = null)
        {
            TextBlock note = null!;
            void ShowNote(int index) => SetHint(note, index >= 0 ? BloodborneLauncher.Core.UiText.T(choices[index].Note) : null);
            void Picked(int index)
            {
                set(choices[index].Value);
                ShowNote(index);
                Ws.SaveSoon();
                changed?.Invoke();
            }

            var shortLabels = choices.Sum(c => Segmented.Short(c.Label).Length);
            if (!SettingRow.Classic && choices.Count is >= 2 and <= 6 && shortLabels <= 72)
            {
                var segmented = new Segmented(choices, enabled);
                var row = Add(Card(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), segmented, out note, wide: true), into);
                segmented.Select(Choices.IndexOf(choices, get()), notify: false);
                ShowNote(segmented.SelectedIndex);
                segmented.Changed += Picked;
                row.Tag = note;
                return segmented;
            }

            var box = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
            foreach (var choice in choices)
                box.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
            var card = Add(Card(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), box, out note), into);
            card.NoteInTooltip = true; note.Tag = card;
            box.SelectedIndex = Choices.IndexOf(choices, get());
            ShowNote(box.SelectedIndex);
            void FitSelection() { if(box.SelectedIndex>=0)box.Width=MenuSizing.SelectorWidth(choices[box.SelectedIndex].Label,width); }
            FitSelection();
            box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) { FitSelection();Picked(box.SelectedIndex); } };
            card.Tag = note;
            return box;
        }

        protected FrameworkElement SettingCombo(SymbolRegular icon, string title, string? hint, IReadOnlyList<Choice> choices,
            string key, Panel? into = null, Action? changed = null) =>
            ComboRow(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), choices, () => S.Str(key), v => S.Set(key, v), into, changed: changed);

        protected FrameworkElement IniCombo(SymbolRegular icon, string title, string? hint, IReadOnlyList<Choice> choices,
            string key, Panel? into = null, Action? changed = null, Func<Choice, bool>? enabled = null) =>
            ComboRow(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), choices, () => Ini.Get(key), v => Ini.Set(key, v), into, changed: changed, enabled: enabled);

        protected ToggleSwitch ToggleRow(SymbolRegular icon, string title, string? hint, Func<bool> get, Action<bool> set,
            Panel? into = null, Action? changed = null)
        {
            var toggle = new ToggleSwitch { IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) =>
            {
                set(toggle.IsChecked == true);
                Ws.SaveSoon();
                changed?.Invoke();
            };
            Add(Card(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), toggle, out _), into);
            return toggle;
        }

        protected ToggleSwitch SettingToggle(SymbolRegular icon, string title, string? hint, string key, Panel? into = null, Action? changed = null) =>
            ToggleRow(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), () => S.Bool(key), v => S.Set(key, v), into, changed);

        protected ToggleSwitch IniToggle(SymbolRegular icon, string title, string? hint, string key, Panel? into = null, Action? changed = null) =>
            ToggleRow(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), () => Ini.Flag(key), v => Ini.Set(key, v ? "1" : "0"), into, changed);

        /// <summary>A slider with its value printed beside it.</summary>
        protected Slider SliderRow(SymbolRegular icon, string title, string? hint, double min, double max, double step,
            Func<double> get, Action<double> set, string format = "0.00", Panel? into = null, double valueWidth = 44)
        {
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                SmallChange = step,
                LargeChange = step * 4,
                TickFrequency = step,
                IsSnapToTickEnabled = true,
                Width = 260,
                VerticalAlignment = VerticalAlignment.Center,
                Value = Math.Clamp(get(), min, max),
            };
            var value = new TextBlock
            {
                MinWidth = valueWidth,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            value.Text = BloodborneLauncher.Core.UiText.T(slider.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
            slider.ValueChanged += (_, _) =>
            {
                value.Text = BloodborneLauncher.Core.UiText.T(slider.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
                set(slider.Value);
                Ws.SaveSoon();
            };
            // Reserve the measured value width before allocating the remaining track space.
            var sliderRow = new Grid { Width = 330, Tag = "slider-value-track", VerticalAlignment = VerticalAlignment.Center };
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            slider.Width = double.NaN;
            slider.MinWidth = 0;
            slider.Margin = new Thickness(6, 0, 6, 0);
            Grid.SetColumn(slider, 1);
            sliderRow.Children.Add(value);
            sliderRow.Children.Add(slider);
            Add(Card(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), sliderRow, out _), into);
            return slider;
        }

        /// <summary>A text field row; saves as you type.</summary>
        protected TextBox TextRow(SymbolRegular icon, string title, string? hint, string placeholder,
            Func<string> get, Action<string> set, Panel? into = null, double width = 300)
        {
            var box = new TextBox
            {
                Text = get(),
                PlaceholderText = BloodborneLauncher.Core.UiText.T(placeholder),
                PlaceholderEnabled = true,
                Width = width,
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) =>
            {
                set(box.Text);
                Ws.SaveSoon();
            };
            Add(Card(icon, BloodborneLauncher.Core.UiText.T(title), BloodborneLauncher.Core.UiText.T(hint), box, out _), into);
            return box;
        }

        protected static string Fmt(double value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        protected static double ParseDouble(string text, double fallback) =>
            double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : fallback;

        protected static string? PickFolder(string title, string current)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = BloodborneLauncher.Core.UiText.T(title) };
            if (System.IO.Directory.Exists(current))
                dialog.InitialDirectory = current;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
