namespace BloodborneLauncher.Native.Backend;
/// <summary>Fallback for combined session logs. Do not confuse launcher setup with process output.</summary>
internal sealed class GameOutputFilter
{
    bool _started;
    public void Reset(bool tail) => _started = tail;
    public bool Accept(string line)
    {
        if (line.StartsWith("Starting:", StringComparison.OrdinalIgnoreCase) && line.Contains("bb-probe", StringComparison.OrdinalIgnoreCase))
        {
            _started = true;
            return false;
        }

        if (line.StartsWith("bb-probe exited with code", StringComparison.OrdinalIgnoreCase) || line.StartsWith("— the game exited", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!_started)
        {
            _started = line.StartsWith("Runtime:", StringComparison.Ordinal) || line.StartsWith("GPU:", StringComparison.Ordinal) || line.StartsWith("GPU [", StringComparison.Ordinal) || line.StartsWith("Streaming:", StringComparison.Ordinal) || line.StartsWith("Window:", StringComparison.Ordinal);
        }

        return _started;
    }
}
