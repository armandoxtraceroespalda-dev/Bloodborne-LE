using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Rect=Vortice.Mathematics.Rect;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    readonly record struct SliderRow(Rect Bounds,int First,int Count,int Hit);
    readonly Dictionary<string,SliderRow> _sliderRows=new(StringComparer.Ordinal);
    readonly List<Op> _sliderOps=new(12);
    readonly List<Hit> _sliderHits=new(1);
    int _sliderHitBase=-1;
    long _sliderRowUpdates;
    bool RefreshSliderRow(Option option)
    {
        if(_dirty || _switchAt>=0 || _messagePrompt!=null || _resetPrompt!=null || _recommendation!=null ||
            !_sliderRows.TryGetValue(option.Key,out var row) || row.First+row.Count>_ops.Count)return false;
        var original=_ops[row.First];
        bool fixedBefore=_fixed,scrollBefore=_scrollContent,surfaceBefore=_surface;
        var clipBefore=_clip;
        _sliderOps.Clear();_sliderHits.Clear();_sliderHitBase=row.Hit;_overlayTarget=_sliderOps;
        _fixed=original.Fixed;_scrollContent=original.Scrollable;_surface=original.Surface;_clip=original.Clip;
        _canvas.BeginPartialLayout();
        try { DrawOption(option,row.Bounds); }
        finally
        {
            _sliderHitBase=-1;_overlayTarget=null;_fixed=fixedBefore;_scrollContent=scrollBefore;_surface=surfaceBefore;_clip=clipBefore;
        }
        if(_sliderOps.Count!=row.Count || _sliderHits.Count!=1)return false;
        for(int i=0;i<row.Count;i++)_ops[row.First+i]=_sliderOps[i];
        _hits[row.Hit]=_sliderHits[0];
        _sliderOps.Clear();_sliderHits.Clear();
        _sliderRowUpdates++;
        SyncSceneResources();
        var screen=ScreenRect(row.Bounds,original.Scrollable);
        Invalidate(new Rect(screen.X-4,screen.Y-4,screen.Width+8,screen.Height+8));
        return true;
    }
}
