using Vortice.Mathematics;

namespace BloodborneLauncher.Native.Interaction;
/// <summary>Scroll the retained content without rebuilding rows or changing text layout keys.</summary>
public static class ScrollGeometry
{
    public static Rect Screen(Rect b, bool scrollable, float offset) => scrollable ? new Rect(b.X, b.Y - offset, b.Width, b.Height) : b;
    public static Rect Thumb(Rect track, float maximum, float offset)
    {
        float height = Math.Max(30, track.Height * track.Height / (track.Height + Math.Max(0, maximum)));
        float y = track.Top + (maximum > 0 ? Math.Clamp(offset, 0, maximum) / maximum : 0) * (track.Height - height);
        return new Rect(track.X, y, track.Width, height);
    }

    public static bool Hit(Rect b, bool scrollable, Rect? clip, float x, float y, float offset) => b.Contains(x, y + (scrollable ? offset : 0)) && (clip == null || clip.Value.Contains(x, y));
}
