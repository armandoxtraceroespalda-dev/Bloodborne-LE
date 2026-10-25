using System.Runtime.InteropServices;

namespace BloodborneLauncher.Native.Platform;

/// <summary>One reclamation pass after committed navigation, never a periodic idle loop.</summary>
internal sealed class NavigationMemoryReclaimer : IDisposable
{
    readonly Action<Action> _queue;
    readonly Action _collect;
    readonly Func<bool> _trim;
    int _pending, _running, _version, _disposed;
    long _collections, _trims, _failures, _collectTicks;
    readonly Func<long> _clock;
    readonly int _minSpacing;
    long _lastStarted=long.MinValue;
    public double CollectionMilliseconds => Interlocked.Read(ref _collectTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;
    public long Collections => Interlocked.Read(ref _collections);
    public long WorkingSetTrims => Interlocked.Read(ref _trims);
    public long Failures => Interlocked.Read(ref _failures);

    public NavigationMemoryReclaimer() : this(
        action => { _ = Task.Run(action); },
        () => GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false),
        () => OperatingSystem.IsWindows() && EmptyWorkingSet(Win32.GetCurrentProcess()), minSpacingMilliseconds:750) { }

    internal NavigationMemoryReclaimer(Action<Action> queue, Action collect, Func<bool> trim, Func<long>? clock=null, int minSpacingMilliseconds=0)
    {
        _clock=clock??(()=>Environment.TickCount64);_minSpacing=minSpacingMilliseconds;
        _queue = queue;
        _collect = collect;
        _trim = trim;
    }

    public void CancelPending()
    {
        Interlocked.Increment(ref _version);
        Interlocked.Exchange(ref _pending, 0);
    }

    public void Request()
    {
        if (Volatile.Read(ref _disposed) == 0)
            Interlocked.Exchange(ref _pending, 1);
    }

    public bool TryStart(bool ready)
    {
        if (!ready || Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _pending) == 0)
            return false;
        long now=_clock();
        if(_lastStarted!=long.MinValue && now-_lastStarted<_minSpacing)return false;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return false;
        if (Interlocked.Exchange(ref _pending, 0) == 0)
        {
            Volatile.Write(ref _running, 0);
            return false;
        }
        _lastStarted=now;
        int version = Volatile.Read(ref _version);
        try
        {
            _queue(() => Reclaim(version));
        }
        catch
        {
            Interlocked.Increment(ref _failures);
            Volatile.Write(ref _running, 0);
        }
        return true;
    }

    bool Current(int version) => Volatile.Read(ref _disposed) == 0 && version == Volatile.Read(ref _version);
    void Reclaim(int version)
    {
        try
        {
            if (!Current(version)) return;
            // Old native resources were disposed on the UI thread before this pass.
            // This does not touch the current renderer or force heap compaction.
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            _collect();
            Interlocked.Add(ref _collectTicks,System.Diagnostics.Stopwatch.GetTimestamp()-started);
            Interlocked.Increment(ref _collections);
            if (Current(version))
            {
                if (_trim()) Interlocked.Increment(ref _trims);
                else Interlocked.Increment(ref _failures);
            }
        }
        catch
        {
            // Reclamation failure must not interrupt navigation or close the launcher.
            Interlocked.Increment(ref _failures);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        CancelPending();
    }

    // This changes resident working-set pages, not the process's committed size.
    // Needed pages may be faulted back in as the user continues to navigate.
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EmptyWorkingSet(nint process);
}
