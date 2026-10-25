namespace BloodborneLauncher.Native.Interaction;
/// <summary>Hover state has no reference to the settings scene or its builder.</summary>
public sealed class PointerState
{
    public int Hover { get; private set; } = -1;
    public int Focus { get; set; } = -1;
    public double Since { get; private set; }
    public bool TooltipShown { get; set; }
    public long Changes { get; private set; }

    public bool Move(int hit, double now)
    {
        if (hit == Hover)
            return false;
        Hover = hit;
        Since = now;
        TooltipShown = false;
        Changes++;
        return true;
    }
}
