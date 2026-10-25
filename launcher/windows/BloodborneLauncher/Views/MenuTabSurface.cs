using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using BloodborneLauncher.Core;
namespace BloodborneLauncher.Views;

/// <summary>Caption-sized oval shade with a reusable, opacity-pulsed native glow.</summary>
public sealed class MenuTabSurface : FrameworkElement
{
    public static readonly DependencyProperty ShowUnderlineProperty=DependencyProperty.RegisterAttached("ShowUnderline",typeof(bool),typeof(MenuTabSurface),new FrameworkPropertyMetadata(true));
    public static bool GetShowUnderline(DependencyObject item)=>(bool)item.GetValue(ShowUnderlineProperty);
    public static void SetShowUnderline(DependencyObject item,bool value)=>item.SetValue(ShowUnderlineProperty,value);
    public static readonly DependencyProperty UnderlineProperty=DependencyProperty.Register(nameof(Underline),typeof(bool),typeof(MenuTabSurface),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Underline {get=>(bool)GetValue(UnderlineProperty);set=>SetValue(UnderlineProperty,value);}
    public static readonly DependencyProperty HighlightProperty=DependencyProperty.Register(nameof(Highlight),typeof(bool),typeof(MenuTabSurface),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender,OnHighlightChanged));
    public static readonly DependencyProperty CaptionWidthProperty=DependencyProperty.Register(nameof(CaptionWidth),typeof(double),typeof(MenuTabSurface),new FrameworkPropertyMetadata(100.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CaptionHeightProperty=DependencyProperty.Register(nameof(CaptionHeight),typeof(double),typeof(MenuTabSurface),new FrameworkPropertyMetadata(32.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Highlight {get=>(bool)GetValue(HighlightProperty);set=>SetValue(HighlightProperty,value);}
    public double CaptionWidth {get=>(double)GetValue(CaptionWidthProperty);set=>SetValue(CaptionWidthProperty,value);}
    public double CaptionHeight {get=>(double)GetValue(CaptionHeightProperty);set=>SetValue(CaptionHeightProperty,value);}
    public static readonly DependencyProperty UnderlineSpanProperty=DependencyProperty.RegisterAttached(
        "UnderlineSpan",typeof(double),typeof(MenuTabSurface),new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
    public static double GetUnderlineSpan(DependencyObject item)=>(double)item.GetValue(UnderlineSpanProperty);
    public static void SetUnderlineSpan(DependencyObject item,double value)=>item.SetValue(UnderlineSpanProperty,value);
    // Retain underline dependency properties for compatibility with existing
    // templates and navigation code, but sidebar underlines are no longer drawn.
    private Brush? _glowSource;
    private Brush? _glow;
    private bool _pulseRunning;
    public MenuTabSurface()
    {
        Loaded+=OnLoaded;
        Unloaded+=OnUnloaded;
    }
    private void OnLoaded(object sender,RoutedEventArgs e)
    {
        UpdatePulse();InvalidateVisual();
    }
    private void OnUnloaded(object sender,RoutedEventArgs e)
    {
        StopPulse();_glow=null;_glowSource=null;
    }
    private static void OnHighlightChanged(DependencyObject sender,DependencyPropertyChangedEventArgs args)
    {
        ((MenuTabSurface)sender).UpdatePulse();
    }
    private void StopPulse()
    {
        _glow?.BeginAnimation(Brush.OpacityProperty,null);
        _pulseRunning=false;
    }
    private void UpdatePulse()
    {
        if(!IsLoaded || !Highlight || _glow==null)
        {
            StopPulse();return;
        }
        if(_pulseRunning)return;
        double high=_glowSource?.Opacity??1;
        // Half a second in each direction gives one complete breathing cycle
        // per second. Animate only the brush, not the text or full tab surface.
        var pulse=new DoubleAnimation(high,high*0.5,TimeSpan.FromMilliseconds(500)) {
            AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,
            EasingFunction=new SineEase {EasingMode=EasingMode.EaseInOut}
        };
        Timeline.SetDesiredFrameRate(pulse,30);pulse.Freeze();
        _glow.BeginAnimation(Brush.OpacityProperty,pulse,HandoffBehavior.SnapshotAndReplace);
        _pulseRunning=true;
    }
    private Brush PulsingGlow(Brush source)
    {
        if(!ReferenceEquals(_glowSource,source) || _glow==null)
        {
            StopPulse();_glowSource=source;_glow=source.Clone();
        }
        UpdatePulse();return _glow!;
    }
    private static readonly Brush Shade=CreateShade();
    private static Brush CreateShade()
    {
        var brush=new RadialGradientBrush { Center=new Point(0.5,0.5),GradientOrigin=new Point(0.5,0.5),RadiusX=0.5,RadiusY=0.5 };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(174,20,22,23),0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(157,20,22,23),0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,20,22,23),1));brush.Freeze();return brush;
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;if(w<1 || h<1)return;
        double center=w/2;
        if(TemplatedParent is Control owner && owner.HorizontalContentAlignment==HorizontalAlignment.Left)
            center=owner.Padding.Left+CaptionWidth/2;
        double reach=Math.Max(0,Math.Min(center-2,w-center-2));
        dc.DrawEllipse(Shade,null,new Point(center,h/2),Math.Min(reach,CaptionWidth/2+36),Math.Min(h/2,CaptionHeight*0.8+8));
        if(Highlight && TryFindResource("MenuGlowBrush") is Brush glow)
        {
            var band=TabMetrics.Glow(w,h,CaptionWidth,CaptionHeight);
            double half=band.Width/2;
            dc.DrawRectangle(PulsingGlow(glow),null,new Rect(center-half,band.Y,2*half,band.Height));
        }
    }
}
