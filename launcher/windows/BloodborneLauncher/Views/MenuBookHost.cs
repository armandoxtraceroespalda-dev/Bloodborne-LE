using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BloodborneLauncher.Views;

/// <summary>One shared native leaf, mirrored around settings with a reserved ink-safe area.</summary>
public sealed class MenuBookHost : Decorator
{
    public static readonly DependencyProperty ActiveProperty=DependencyProperty.Register(nameof(Active),typeof(bool),typeof(MenuBookHost),
        new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsArrange|FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Active { get=>(bool)GetValue(ActiveProperty);set=>SetValue(ActiveProperty,value); }
    public static readonly DependencyProperty ArtOpacityProperty=DependencyProperty.Register(nameof(ArtOpacity),typeof(double),typeof(MenuBookHost),
        new FrameworkPropertyMetadata(1.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public double ArtOpacity {get=>(double)GetValue(ArtOpacityProperty);set=>SetValue(ArtOpacityProperty,value);}
    public static readonly DependencyProperty FollowPageFadeProperty=DependencyProperty.Register(nameof(FollowPageFade),typeof(bool),typeof(MenuBookHost),
        new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
    public bool FollowPageFade {get=>(bool)GetValue(FollowPageFadeProperty);set=>SetValue(FollowPageFadeProperty,value);}
    private ImageBrush? _panel;
    private ImageBrush? _panelSource;
    private ImageSource? _image;
    private ImageBrush[]? _slices;
    private const int InnerBands=64;
    private MatrixTransform? _mirror;
    private double _mirrorAxis=double.NaN;

    private static (Rect Book,Rect Ink,double Edge) Layout(Size size)
    {
        double w=Math.Max(0,size.Width),h=Math.Max(0,size.Height);
        var book=new Rect(8,38,Math.Max(0,w-20),Math.Max(0,h-38));
        // Keep the paper/binding at a bounded width rather than stretching it
        // across a large display. Only the quiet inner part expands horizontally.
        double edge=Math.Min(144,book.Width*0.15);
        // Only the actual page stack is reserved. Text may occupy the dark,
        // textured ragged area; the narrower binding leaves more room for settings.
        double inset=edge*(72.0/240)+4,top=10,bottom=8;
        var ink=new Rect(book.Left+inset,book.Top+top,Math.Max(0,book.Width-2*inset),Math.Max(0,book.Height-top-bottom));
        return(book,ink,edge);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if(Child==null)return new Size();
        if(!Active){Child.Measure(availableSize);return Child.DesiredSize;}
        // The window has finite dimensions. Fall back safely for design-time or
        // unconstrained hosts, without introducing an infinite ScrollViewer size.
        var finite=new Size(double.IsFinite(availableSize.Width)?availableSize.Width:1000,
                            double.IsFinite(availableSize.Height)?availableSize.Height:700);
        var layout=Layout(finite);Child.Measure(layout.Ink.Size);
        return finite;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout=Layout(finalSize);
        Child?.Arrange(Active?layout.Ink:new Rect(finalSize));
        // Move only the page's main scrollbar, not nested keybinding or popup
        // scrollbars. Text stays inside Ink while the scrollbar sits on paper.
        if(Child is ContentControl {Content:LauncherPage page})
            page.MoveBookScrollBar(Active?Math.Max(0,layout.Ink.Left-layout.Book.Left-2):0);
        return finalSize;
    }
    private static ImageBrush Slice(ImageSource image,Rect region) => new(image) {
        Viewbox=region,ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Stretch=Stretch.Fill
    };
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(!Active || ActualWidth<40 || ActualHeight<100 ||
            TryFindResource("MenuBookLeafBrush") is not ImageBrush {ImageSource:{ } image} original)return;
        if(!ReferenceEquals(_image,image))
        {
            _image=image;_slices=new ImageBrush[InnerBands+1];
            _slices[0]=Slice(image,new Rect(0,0,240.0/472,1));
            // Small source strips fade the inner texture without a window-size
            // opacity mask, extra decoded bitmap, or per-frame brush allocation.
            for(int i=0;i<InnerBands;i++)
                _slices[i+1]=Slice(image,new Rect((240+232.0*i/InnerBands)/472,0,232.0/InnerBands/472,1));
        }
        double opacity=original.Opacity*(FollowPageFade?Math.Clamp(ArtOpacity,0,1):1);
        _slices![0].Opacity=opacity;
        for(int i=0;i<InnerBands;i++)
        {
            double t=(i+0.5)/InnerBands;
            double fade=Math.Clamp((t-0.35)/0.65,0,1);
            fade=fade*fade*(3-2*fade);
            _slices[i+1].Opacity=opacity*(1-fade);
        }
        var layout=Layout(RenderSize);var book=layout.Book;
        // Cover the book frame, including the upper band and binding.
        // Rounded corners follow the paper edges; the leaves remain
        // above the backing and retain their existing Start-only fade.
        if(TryFindResource("MenuBookPanelBrush") is ImageBrush panel)
        {
            if(!ReferenceEquals(_panelSource,panel))
            {
                _panelSource=panel;_panel=panel.Clone();
            }
            _panel!.Opacity=panel.Opacity*(FollowPageFade?Math.Clamp(ArtOpacity,0,1):1);
            // Follow the rounded paper edges instead of exposing a square
            // panel corner outside the leaf. Keep the upper frame covered.
            double insetX=layout.Edge*0.06,insetY=book.Height*0.005;
            var backing=new Rect(book.Left+insetX,book.Top+insetY,
                Math.Max(0,book.Width-2*insetX),Math.Max(0,book.Height-2*insetY));
            double corner=Math.Min(layout.Edge*0.24,backing.Height*0.08);
            dc.DrawRoundedRectangle(_panel,null,backing,corner,corner);
        }
        void Leaf() {
            dc.DrawRectangle(_slices![0],null,new Rect(book.Left,book.Top,layout.Edge,book.Height));
            double inner=Math.Max(0,book.Width/2-layout.Edge);
            for(int i=0;i<InnerBands;i++)
            {
                double left=book.Left+layout.Edge+inner*i/InnerBands;
                double right=book.Left+layout.Edge+inner*(i+1)/InnerBands;
                dc.DrawRectangle(_slices[i+1],null,new Rect(left,book.Top,right-left,book.Height));
            }
        }
        Leaf();
        double axis=book.Left+book.Right;
        if(_mirrorAxis!=axis){_mirrorAxis=axis;_mirror=new MatrixTransform(-1,0,0,1,axis,0);_mirror.Freeze();}
        dc.PushTransform(_mirror!);Leaf();dc.Pop();
    }
}
