using System.Reflection;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class OptimizationTests
{
    public static void Run(string root)
    {
        static void Check(bool ok, string text)
        {
            if (!ok) throw new InvalidOperationException(text);
            Console.WriteLine("PASS " + text);
        }

        var stats = new SessionStats();
        Check(!stats.HasFrameStats && stats.AverageFps == 0 && stats.LowestFps == 0,
            "empty session statistics retain the original public behavior");
        double sum = 0, lowest = double.PositiveInfinity;
        for (int i = 0; i < 10000; i++)
        {
            int fps = 30 + i % 121;
            sum += fps; lowest = Math.Min(lowest, fps);
            stats.Observe($"Frame stats: {fps} FPS, worst frame 12.5 ms");
        }
        stats.Observe("Stall: 200 ms frame");
        Check(stats.HasFrameStats && stats.AverageFps == sum / 10000 && stats.LowestFps == lowest &&
            stats.WorstFrameMs == 200 && stats.Stutters == 1,
            "10000 FPS samples preserve average, minimum, worst frame and stutter statistics");
        Check(!typeof(SessionStats).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(field => typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType)),
            "session statistics retain scalar aggregates instead of a growing sample collection");

        string file = Path.Combine(root, "allocation-log.log");
        File.WriteAllText(file, string.Concat(Enumerable.Repeat(new string('x', 1000) + "\n", 1900)));
        var log = new LiveOutputBuffer();
        while (log.Position < new FileInfo(file).Length) log.Poll(file);
        string snapshot = log.Text(); // Warm the static string.Create callback.
        Check(snapshot.EndsWith(Environment.NewLine) && snapshot.Length == 1900 * (1000 + Environment.NewLine.Length),
            "single-allocation log snapshots retain every line and the trailing newline");
        log.ReleaseSnapshot();
        long before = GC.GetAllocatedBytesForCurrentThread();
        string second = log.Text();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"METRIC log snapshot: {second.Length} characters, {allocated} allocated bytes");
        Check(second == snapshot && allocated <= second.Length * 2L + 256,
            "a 1.9-million-character log snapshot allocates only its final string");
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) _ = log.Text();
        Check(GC.GetAllocatedBytesForCurrentThread() == before,
            "repeated access to an unchanged log allocates no additional snapshots");

        string previous = "old\r\nretained one\r\nretained two\r\n";
        string next = "retained one\r\nretained two\r\nnew\r\n";
        Check(TextSplice.TryRollingAppend(previous, next, 5, out var drop, out var append),
            "rolling log history detects an exact retained suffix using the discarded-prefix count");
        string edited = previous.Remove(drop.Start, drop.Removed)
            .Insert(append.Start, next.Substring(append.Start, append.Added));
        Check(edited == next && append.Removed == 0 && append.Added == 5,
            "rolling logs delete only expired history and insert only new output");
        Check(append.MapSelection(drop.MapSelection(10)) == 5,
            "rolling log updates keep a selected position anchored in retained text");
        Check(!TextSplice.TryRollingAppend(previous, "different session", 5, out _, out _) &&
            !TextSplice.TryRollingAppend(previous, next, -1, out _, out _),
            "replaced or reset log sessions fall back safely instead of trusting stale rollover hints");
        File.AppendAllText(file, new string('y', 1000) + "\n");
        log.Poll(file);
        Check(log.RemovedCharacters == 0, "short histories do not report discarded text");
        for (int i = 0; i < 500; i++) File.AppendAllText(file, new string('z', 1000) + "\n");
        long size = new FileInfo(file).Length;
        while (log.Position < size) log.Poll(file);
        Check(log.RemovedCharacters == (2401L - log.Lines.Count) * (1000 + Environment.NewLine.Length) && log.RemovedCharacters > 0,
            "bounded log history reports the exact expired prefix for incremental native updates");
        log.Clear();
        Check(log.RemovedCharacters == 0 && log.Text() == "",
            "clearing log history resets rollover accounting and snapshots together");
    }
}
