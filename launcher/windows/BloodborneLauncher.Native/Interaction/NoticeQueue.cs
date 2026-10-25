namespace BloodborneLauncher.Native.Interaction;

/// <summary>Bounded FIFO of lightweight notices; never allocates UI resources while waiting.</summary>
internal sealed class NoticeQueue<T>
{
    readonly Queue<T> _pending = new();
    readonly int _capacity;
    public NoticeQueue(int capacity) { ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1); _capacity = capacity; }
    public int Count => _pending.Count;
    public bool Enqueue(T item)
    {
        if (_pending.Count >= _capacity) return false;
        _pending.Enqueue(item);
        return true;
    }
    public bool TryTake(out T item) => _pending.TryDequeue(out item!);
    public bool Contains(Predicate<T> predicate)
    {
        foreach (var item in _pending) if (predicate(item)) return true;
        return false;
    }
    public void Clear() { _pending.Clear(); _pending.TrimExcess(); }
}
