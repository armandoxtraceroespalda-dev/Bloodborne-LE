using Rect = Vortice.Mathematics.Rect;
namespace BloodborneLauncher.Native.Rendering;
internal static class DialogPlacement
{
    public static Rect Book(float sidebar, float width, float height)
    {
        float left = sidebar - 8;
        return new Rect(left, 38, Math.Max(1, width - left - 12), Math.Max(1, height - 38));
    }
    public static Rect Center(Rect area, float width, float height) =>
        new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
}
