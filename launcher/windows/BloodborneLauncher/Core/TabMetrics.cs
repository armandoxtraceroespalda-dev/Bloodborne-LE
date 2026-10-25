namespace BloodborneLauncher.Core;

public readonly record struct TabGlowBounds(double X,double Y,double Width,double Height);
public static class TabMetrics
{
    public static double SidebarScale(double windowWidth,double windowHeight)=>
        Math.Clamp(Math.Min(windowWidth/1280.0,windowHeight/800.0),0.85,1.6);

    // Row height changes on maximize; the light band's aspect must follow the caption instead.
    public static TabGlowBounds Glow(double width,double height,double captionWidth,double captionHeight)
    {
        // Extend the transparent wings past the button, including into the
        // neighboring settings area. The visual is never hit-testable.
        double w=Math.Max(0,width*1.5);
        double h=Math.Min(height,Math.Max(48,captionHeight*1.8));
        return new((width-w)/2,(height-h)/2,w,h);
    }
}
