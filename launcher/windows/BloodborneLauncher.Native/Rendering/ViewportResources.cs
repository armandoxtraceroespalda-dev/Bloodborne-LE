using BloodborneLauncher.Native.Interaction;
using Vortice.Mathematics;

namespace BloodborneLauncher.Native.Rendering;
/// <summary>Retain native resources only around the visible viewport.</summary>
internal static class ViewportResources
{
    public static bool Keep(Op op, float scroll) => op.Kind == Ink.Text && Visible(op, scroll);
    public static bool Visible(Op op, float scroll)
    {
        if (!op.Scrollable || op.Clip is not Rect clip) return true;
        var screen = ScrollGeometry.Screen(op.Bounds, true, scroll);
        const float overscan = 32;
        if (!Canvas.Intersects(screen, new Rect(clip.X, clip.Y - overscan, clip.Width, clip.Height + overscan * 2))) return false;
        return op.ContentClip is not Rect content || Canvas.Intersects(screen, ScrollGeometry.Screen(content, true, scroll));
    }
}
