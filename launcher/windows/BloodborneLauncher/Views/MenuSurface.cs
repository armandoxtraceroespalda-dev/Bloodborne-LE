using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace BloodborneLauncher.Views;

/// <summary>Cached gradient shadow, with optional native trim; no per-frame blur effect.</summary>
public sealed class MenuSurface : Border
{
    public static readonly DependencyProperty SuppressSectionShadowProperty=DependencyProperty.RegisterAttached(
        "SuppressSectionShadow",typeof(bool),typeof(MenuSurface),new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
    public static bool GetSuppressSectionShadow(DependencyObject item)=>(bool)item.GetValue(SuppressSectionShadowProperty);
    public static void SetSuppressSectionShadow(DependencyObject item,bool value)=>item.SetValue(SuppressSectionShadowProperty,value);
    public static readonly DependencyProperty KeybindingsShadowProperty=DependencyProperty.RegisterAttached(
        "KeybindingsShadow",typeof(bool),typeof(MenuSurface),new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender));
    public static bool GetKeybindingsShadow(DependencyObject item)=>(bool)item.GetValue(KeybindingsShadowProperty);
    public static void SetKeybindingsShadow(DependencyObject item,bool value)=>item.SetValue(KeybindingsShadowProperty,value);
    public bool Sidebar { get; set; }
    public bool Framed { get; set; }
    public double ShadowOpacity { get; set; } = 1;
    // Nine small shared image slices avoid a full-size intermediate opacity-mask surface.
    private static readonly ImageBrush[] Shadow = CreateShadow();
    private static readonly ImageBrush[] KeyShadow = CreateShadow(true);
    private static readonly Brush SidebarShadow = CreateSidebarShadow();
    private static ImageBrush[] CreateShadow(bool black=false)
    {
        const int size=64, edge=16;
        var pixels=new byte[size*size*4];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            double ax=System.Math.Min(1,System.Math.Min(x,size-1-x)/(double)edge);
            double ay=System.Math.Min(1,System.Math.Min(y,size-1-y)/(double)edge);
            int index=(y*size+x)*4;
            byte alpha=(byte)System.Math.Round(210*0.85*(black?0.5625:1)*ax*ay);
            // Pbgra32 requires premultiplied RGB, including transparent edges.
            pixels[index]=(byte)System.Math.Round((black?0:23)*alpha/255.0);
            pixels[index+1]=(byte)System.Math.Round((black?0:22)*alpha/255.0);
            pixels[index+2]=(byte)System.Math.Round((black?0:20)*alpha/255.0);
            pixels[index+3]=alpha;
        }
        var image=System.Windows.Media.Imaging.BitmapSource.Create(size,size,96,96,PixelFormats.Pbgra32,null,pixels,size*4);
        image.Freeze();var slices=new ImageBrush[9];
        int[] cuts={0,edge,size-edge,size};
        for(int y=0;y<3;y++)for(int x=0;x<3;x++)
        {
            var brush=new ImageBrush(image) { ViewboxUnits=BrushMappingMode.Absolute,
                Viewbox=new Rect(cuts[x],cuts[y],cuts[x+1]-cuts[x],cuts[y+1]-cuts[y]),Stretch=Stretch.Fill };
            brush.Freeze();slices[y*3+x]=brush;
        }
        return slices;
    }
    private static Brush CreateSidebarShadow()
    {
        var brush=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(200,20,22,23),0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(200,20,22,23),0.88));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,20,22,23),1));brush.Freeze();return brush;
    }
    private ImageSource? _frameImage;
    private double _flipHeight = double.NaN;
    private MatrixTransform? _flip;
    private ImageBrush[]? _frameSlices;
    private double _shadowOpacity = double.NaN;
    private ImageBrush[] _shadowBrushes = Shadow;
    private ImageBrush[] ShadowBrushes()
    {
        if(GetKeybindingsShadow(this))return KeyShadow;
        double strength = System.Math.Clamp(ShadowOpacity, 0, 1);
        if (strength == _shadowOpacity) return _shadowBrushes;
        _shadowOpacity = strength;
        if (strength == 1) return _shadowBrushes = Shadow;
        _shadowBrushes = Shadow.Select(source => {
            var brush = source.Clone(); brush.Opacity = strength; brush.Freeze(); return brush;
        }).ToArray();
        return _shadowBrushes;
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w=ActualWidth,h=ActualHeight;if(w<1 || h<1)return;
        bool showShadow=!GetSuppressSectionShadow(this)||GetKeybindingsShadow(this);
        if(showShadow && Sidebar) dc.DrawRectangle(SidebarShadow,null,new Rect(0,0,w,h));
        else if(showShadow)
        {
            // Brush alpha avoids an intermediate group-opacity render surface.
            var shadows = ShadowBrushes();
            double ex=System.Math.Min(26,w/2),ey=System.Math.Min(20,h/2);
            Span<double> xs=stackalloc double[]{0,ex,w-ex,w};
            Span<double> ys=stackalloc double[]{0,ey,h-ey,h};
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                if(xs[x+1]>xs[x] && ys[y+1]>ys[y])
                    dc.DrawRectangle(shadows[y*3+x],null,new Rect(xs[x],ys[y],xs[x+1]-xs[x],ys[y+1]-ys[y]));
        }
        if(Framed && TryFindResource("MenuFrameBrush") is Brush frame)
        {
            DrawTrim(dc,frame,w);
            if (_flipHeight != h) { _flipHeight=h; _flip=new MatrixTransform(1,0,0,-1,0,h);_flip.Freeze(); }
            dc.PushTransform(_flip!);DrawTrim(dc,frame,w);dc.Pop();
        }
    }
    private void DrawTrim(DrawingContext dc,Brush frame,double width)
    {
        if(frame is not ImageBrush { ImageSource: { } image })return;
        if(!ReferenceEquals(image,_frameImage))
        {
            _frameImage=image;_frameSlices=new ImageBrush[3];
            var regions=new[]{new Rect(0,0,70.0/480,1),new Rect(70.0/480,0,340.0/480,1),new Rect(410.0/480,0,70.0/480,1)};
            for(int i=0;i<3;i++){var brush=new ImageBrush(image){Viewbox=regions[i],ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Stretch=Stretch.Fill};brush.Freeze();_frameSlices[i]=brush;}
        }
        double full=System.Math.Max(0,width-16),center=System.Math.Min(full,340.0/58*26),side=(full-center)/2;
        dc.DrawRectangle(_frameSlices![0],null,new Rect(8,0,side,26));
        dc.DrawRectangle(_frameSlices[1],null,new Rect(8+side,0,center,26));
        dc.DrawRectangle(_frameSlices[2],null,new Rect(8+side+center,0,side,26));
    }
}
