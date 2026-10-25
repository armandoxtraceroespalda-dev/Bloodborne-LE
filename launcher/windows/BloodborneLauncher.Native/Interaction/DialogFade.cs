namespace BloodborneLauncher.Native.Interaction;

/// <summary>Clock-driven modal opacity; no per-frame allocations or scene rebuilds.</summary>
internal sealed class DialogFade
{
    public const double Duration = .18;
    double _started;
    float _from = 1, _to = 1;
    public bool Animating { get; private set; }
    public float Value(double now)
    {
        if (!Animating) return _to;
        float t = (float)Math.Clamp((now - _started) / Duration, 0, 1);
        float eased = t * t * (3 - 2 * t);
        return _from + (_to - _from) * eased;
    }
    public void Open(double now) { _started = now; _from = 0; _to = 1; Animating = true; }
    public void Close(double now) { _from = Value(now); _started = now; _to = 0; Animating = true; }
    public bool Complete(double now)
    {
        if (!Animating || now - _started < Duration) return false;
        Animating = false;
        return true;
    }
}
