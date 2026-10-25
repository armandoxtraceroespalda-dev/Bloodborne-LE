using System.Text;

namespace BloodborneLauncher.Native.Backend;
/// <summary>Read the original backend's session log incrementally, with bounded resident history.</summary>
internal sealed class LiveOutputBuffer
{
    public const int MaxLines = 5000, MaxCharacters = 2 * 1024 * 1024, MaxLine = 4096;
    readonly bool _gameOnly;
    readonly GameOutputFilter _game = new();
    readonly bool _requireSession;
    Dictionary<string, SessionBoundary>? _sessionBoundaries;
    public LiveOutputBuffer(bool gameOnly = false, bool requireSession = false)
    {
        _gameOnly = gameOnly;
        _requireSession = requireSession;
    }

    // Snapshot candidate streams before dispatching Play. Old files remain untouched.
    public void BeginSession(IEnumerable<string> paths)
    {
        Reset();
        _path = "";
        _write = _creation = 0;
        _sessionBoundaries = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _sessionBoundaries[path] = SessionBoundary.Capture(info, stream);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    sealed record SessionBoundary(long Length, long Creation, byte[] Head, byte[] Tail)
    {
        public static SessionBoundary Capture(FileInfo info, FileStream stream)
        {
            int count = (int)Math.Min(256, stream.Length);
            byte[] head = new byte[count], tail = new byte[count];
            stream.ReadExactly(head);
            long length = stream.Length;
            stream.Position = length - count;
            stream.ReadExactly(tail);
            return new(length, info.CreationTimeUtc.Ticks, head, tail);
        }

        public bool IsOriginalPrefix(FileInfo info, FileStream stream)
        {
            if (stream.Length < Length || (OperatingSystem.IsWindows() && info.CreationTimeUtc.Ticks != Creation))
                return false;
            Span<byte> sample = stackalloc byte[256];
            stream.Position = 0;
            stream.ReadExactly(sample[..Head.Length]);
            if (!sample[..Head.Length].SequenceEqual(Head))
                return false;
            stream.Position = Length - Tail.Length;
            stream.ReadExactly(sample[..Tail.Length]);
            return sample[..Tail.Length].SequenceEqual(Tail);
        }
    }

    const int BatchBytes = 256 * 1024, InitialBytes = 4 * 1024 * 1024;
    readonly byte[] _bytes = new byte[16384];
    readonly char[] _characters = new char[Encoding.UTF8.GetMaxCharCount(16384)];
    readonly StringBuilder _partial = new();
    readonly byte[] _head = new byte[256];
    int _headLength;
    readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    readonly List<string> _lines = new();
    string _path = "";
    long _position, _creation, _write;
    int _residentCharacters;
    public long RemovedCharacters { get; private set; }
    string? _textCache;
    bool _discardFirst;
    public IReadOnlyList<string> Lines => _lines;
    public string PathRead => _path;
    public long Position => _position;

    public bool Poll(string path)
    {
        if (_requireSession && _sessionBoundaries == null)
            return false;
        var info = new FileInfo(path);
        if (!info.Exists)
            return false;
        long creation = info.CreationTimeUtc.Ticks, write = info.LastWriteTimeUtc.Ticks;
        if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) && info.Length == _position && write == _write)
            return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (_sessionBoundaries != null && _sessionBoundaries.TryGetValue(path, out var boundary))
        {
            bool appended = boundary.IsOriginalPrefix(info, stream);
            if (appended && stream.Length == boundary.Length)
                return false;
            _sessionBoundaries.Remove(path);
            if (appended)
            {
                Reset();
                _path = path;
                _creation = creation;
                _position = boundary.Length;
                _game.Reset(false);
                stream.Position = 0;
                _headLength = stream.Read(_head, 0, (int)Math.Min(_head.Length, boundary.Length));
            }
        }

        stream.Position = 0;
        bool headerChanged = false;
        if (_headLength > 0)
        {
            Span<byte> sample = stackalloc byte[256];
            int read = stream.Read(sample[.._headLength]);
            headerChanged = read != _headLength || !sample[..read].SequenceEqual(_head.AsSpan(0, read));
        }

        bool changed = false;
        if (!string.Equals(_path, path, StringComparison.OrdinalIgnoreCase) || (OperatingSystem.IsWindows() && creation != _creation) || headerChanged || info.Length < _position)
        {
            changed = _lines.Count > 0;
            Reset();
            _path = path;
            _creation = creation;
            _position = Math.Max(0, info.Length - InitialBytes);
            _discardFirst = _position > 0;
            _game.Reset(_discardFirst);
        }

        _write = write;
        if (info.Length == _position)
            return changed;
        if (_headLength == 0)
        {
            stream.Position = 0;
            _headLength = stream.Read(_head, 0, (int)Math.Min(_head.Length, info.Length));
        }

        stream.Position = _position;
        int budget = BatchBytes;
        while (budget > 0)
        {
            int count = stream.Read(_bytes, 0, Math.Min(_bytes.Length, budget));
            if (count == 0)
                break;
            _position += count;
            budget -= count;
            int chars = _decoder.GetChars(_bytes, 0, count, _characters, 0, false);
            for (int i = 0; i < chars; i++)
            {
                char value = _characters[i];
                if (value == '\n')
                {
                    if (!_discardFirst)
                    {
                        string line = _partial.ToString();
                        if (!_gameOnly || !Path.GetFileName(_path).Equals("last_session.log", StringComparison.OrdinalIgnoreCase) || _game.Accept(line))
                        {
                            _lines.Add(line);
                            _residentCharacters += line.Length;
                            changed = true;
                        }
                    }

                    _partial.Clear();
                    _discardFirst = false;
                }
                else if (value != '\r' && value != '\uFEFF' && !_discardFirst && _partial.Length < MaxLine)
                    _partial.Append(value);
            }
        }

        int remove = 0;
        while (_lines.Count - remove > MaxLines || _residentCharacters > MaxCharacters)
            _residentCharacters -= _lines[remove++].Length;
        if (remove > 0)
        {
            for (int i = 0; i < remove; i++)
                RemovedCharacters += _lines[i].Length + Environment.NewLine.Length;
            _lines.RemoveRange(0, remove);
        }
        if (changed)
            _textCache = null;
        return changed;
    }

    public void Clear()
    {
        _textCache = null;
        _lines.Clear();
        _partial.Clear();
        _residentCharacters = 0;
        RemovedCharacters = 0;
        _decoder.Reset();
        _discardFirst = false;
        // Skip everything that existed at Clear, including unread history. Never truncate the file.
        if (_path.Length > 0)
        {
            var info = new FileInfo(_path);
            if (info.Exists)
            {
                _position = info.Length;
                _write = info.LastWriteTimeUtc.Ticks;
                _creation = info.CreationTimeUtc.Ticks;
            }
        }
    }

    void Reset()
    {
        _textCache = null;
        _lines.Clear();
        _residentCharacters = 0;
        RemovedCharacters = 0;
        _headLength = 0;
        _position = 0;
        _partial.Clear();
        _decoder.Reset();
        _discardFirst = false;
    }

    // Drop the duplicate joined view when leaving Log; retain the actual session history.
    public void ReleaseSnapshot() => _textCache = null;

    public string Text()
    {
        if (_textCache != null) return _textCache;
        if (_lines.Count == 0) return _textCache = "";
        // Allocate only the final snapshot. Join + concatenation used to keep
        // an additional document-sized temporary alive on every update.
        int length = checked(_residentCharacters + _lines.Count * Environment.NewLine.Length);
        return _textCache = string.Create(length, _lines, static (destination, lines) =>
        {
            int at = 0;
            foreach (string line in lines)
            {
                line.AsSpan().CopyTo(destination[at..]);
                at += line.Length;
                Environment.NewLine.AsSpan().CopyTo(destination[at..]);
                at += Environment.NewLine.Length;
            }
        });
    }
}
