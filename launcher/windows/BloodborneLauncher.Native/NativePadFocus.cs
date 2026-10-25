using Rect=Vortice.Mathematics.Rect;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    bool _usingPadNavigation;
    Rect? PadFocusRect
    {
        get
        {
            if(!_usingPadNavigation || _switchAt>=0 || _focus<0 || _focus>=_hits.Count)return null;
            var hit=_hits[_focus];if(!PadHit(hit))return null;
            var box=ScreenRect(hit.Bounds,hit.Scrollable);
            if(hit.Scrollable && !Rendering.Canvas.Intersects(box,_viewport))return null;
            return new Rect(box.X-12*_scale,box.Y-6*_scale,box.Width+24*_scale,box.Height+12*_scale);
        }
    }
    void EndPadCue(){if(PadFocusRect is Rect box)Invalidate(box);_usingPadNavigation=false;}
}
