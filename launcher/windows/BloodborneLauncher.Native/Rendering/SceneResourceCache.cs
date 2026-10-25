namespace BloodborneLauncher.Native.Rendering;
/// <summary>Own resources for the current scene; release stale entries immediately.</summary>
public sealed class SceneResourceCache<TKey, TValue> : IDisposable where TKey : notnull where TValue : IDisposable
{
    private sealed class Entry
    {
        public required TValue Value;
        public long Generation;
    }

    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly List<TKey> _stale = new();
    private long _generation;
    public int Count => _entries.Count;
    public long Created { get; private set; }
    public long Released { get; private set; }

    public void BeginScene() => _generation++;
    public TValue Use(TKey key, Func<TKey, TValue> create)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry
            {
                Value = create(key),
                Generation = _generation
            };
            _entries.Add(key, entry);
            Created++;
        }

        entry.Generation = _generation;
        return entry.Value;
    }

    public TValue Get(TKey key) => _entries[key].Value;
    public void EndScene()
    {
        _stale.Clear();
        foreach (var pair in _entries)
            if (pair.Value.Generation != _generation)
                _stale.Add(pair.Key);
        foreach (var key in _stale)
        {
            _entries[key].Value.Dispose();
            _entries.Remove(key);
            Released++;
        }

        _stale.Clear();
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Value.Dispose();
            Released++;
        }

        _entries.Clear();
        _stale.Clear();
    }

    public void Dispose() => Clear();
}
