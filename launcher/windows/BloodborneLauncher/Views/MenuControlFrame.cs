using System.Windows;
using System.Windows.Media;
namespace BloodborneLauncher.Views;

/// <summary>Side-faded dark controls with native underlines and one continuous highlight.</summary>
public sealed class MenuControlFrame : FrameworkElement
{
    public static readonly DependencyProperty HighlightProperty=DependencyProperty.Register(nameof(Highlight),typeof(bool),typeof(MenuControlFrame),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Highlight { get=>(bool)GetValue(HighlightProperty);set=>SetValue(HighlightProperty,value); }
    private static readonly Brush Surface=CreateSurface();
    private static Brush CreateSurface()
    {
        var b=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0,20,22,23),0));b.GradientStops.Add(new GradientStop(Color.FromArgb(208,20,22,23),0.1));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(208,20,22,23),0.9));b.GradientStops.Add(new GradientStop(Color.FromArgb(0,20,22,23),1));b.Freeze();return b;
    }
    private double _trimWidth = double.NaN, _trimHeight = double.NaN;
    private MatrixTransform? _bottom, _mirror;
    private void CacheTransforms(double width, double height)
    {
        if (_trimWidth == width && _trimHeight == height) return;
        _trimWidth = width; _trimHeight = height;
        _bottom = new MatrixTransform(1,0,0,-1,0,height); _bottom.Freeze();
        _mirror = new MatrixTransform(-1,0,0,1,width,0); _mirror.Freeze();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<2 || h<2)return;
        dc.DrawRectangle(Surface,null,new Rect(0,0,w,h));
        if(Highlight && TryFindResource("MenuGlowBrush") is Brush glow)
        {dc.PushOpacity(0.8);dc.DrawRectangle(glow,null,new Rect(0,0,w,h));dc.Pop();}
        if(TryFindResource("MenuUnderlineBrush") is Brush trim)
        {
            CacheTransforms(w,h);dc.PushTransform(_bottom!);Pair(dc,trim,w,-1,10);dc.Pop();
        }
    }
    private void Pair(DrawingContext dc,Brush brush,double width,double y,double height)
    {
        var half=new Rect(0,y,width/2,height);dc.DrawRectangle(brush,null,half);
        dc.PushTransform(_mirror!);dc.DrawRectangle(brush,null,half);dc.Pop();
    }
}
