namespace BloodborneLauncher.Native.Backend;

/// <summary>A single bounded edit; calculating it does not copy the retained log.</summary>
internal readonly record struct TextSplice(int Start, int Removed, int Added)
{
    public static TextSplice Between(string previous, string next)
    {
        int start = 0, common = Math.Min(previous.Length, next.Length);
        while (start < common && previous[start] == next[start]) start++;
        int tail = 0;
        while (tail < common - start && previous[previous.Length - tail - 1] == next[next.Length - tail - 1]) tail++;
        return new(start, previous.Length - start - tail, next.Length - start - tail);
    }

    // The buffer supplies the exact discarded prefix; verify the retained text
    // before using two small edits instead of rewriting a full rolling log.
    public static bool TryRollingAppend(string previous, string next, long retired, out TextSplice drop, out TextSplice append)
    {
        drop = append = default;
        if (retired <= 0 || retired > previous.Length) return false;
        int kept = previous.Length - (int)retired;
        if (next.Length < kept || !previous.AsSpan((int)retired).SequenceEqual(next.AsSpan(0, kept))) return false;
        drop = new(0, (int)retired, 0);
        append = new(kept, 0, next.Length - kept);
        return true;
    }

    public int MapSelection(int position) => position <= Start ? position :
        position >= Start + Removed ? position - Removed + Added : Start + Added;
}
