namespace BloodborneLauncher.Native.Platform;

/// <summary>Coalesce actual log changes into a cleanup after reading/scrolling settles.</summary>
internal sealed class LogMemoryActivity
{
    bool _pending;
    double _firstActivity, _lastActivity, _lastRequest = double.NegativeInfinity;
    public void Changed(double now)
    {
        if (!_pending) _firstActivity = now;
        _pending = true;
        _lastActivity = now;
    }
    public void Cancel() => _pending = false;
    public bool TakeRequest(double now, bool ready)
    {
        if (!_pending || !ready || (now - _lastActivity < .8 && now - _firstActivity < 3) || now - _lastRequest < 10) return false;
        _pending = false;
        _lastRequest = now;
        return true;
    }
}
