using Vortice.Mathematics;

namespace BloodborneLauncher.Native.Rendering;
internal static class WallpaperFraming
{
    public static Rect Crop(float imageWidth, float imageHeight, float viewWidth, float viewHeight, float centerX = -1, float centerY = -1)
    {
        // Anchor the artwork to the book rather than the complete window.
        // Overscan symmetrically about that anchor to keep every edge covered.
        centerX = centerX < 0 ? viewWidth / 2 : Math.Clamp(centerX,0,viewWidth);
        centerY = centerY < 0 ? viewHeight / 2 : Math.Clamp(centerY,0,viewHeight);
        float ratio = Math.Max(2*Math.Max(centerX,viewWidth-centerX) / imageWidth, 2*Math.Max(centerY,viewHeight-centerY) / imageHeight);
        float width = viewWidth / ratio, height = viewHeight / ratio;
        return new Rect(Math.Clamp(imageWidth/2-centerX/ratio,0,Math.Max(0,imageWidth-width)),
            Math.Clamp(imageHeight/2-centerY/ratio,0,Math.Max(0,imageHeight-height)), width, height);
    }
}
