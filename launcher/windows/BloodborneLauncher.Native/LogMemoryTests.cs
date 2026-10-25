using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;

namespace BloodborneLauncher.Native;
internal static class LogMemoryTests
{
    public static void Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        var random = new Random(75);
        string previous = "";
        for (int i = 0; i < 2000; i++)
        {
            string next = i % 5 == 0 ? "" : previous + "\r\nRuntime: " + random.Next() + " λ漢字";
            if (next.Length > 3000) next = next[1000..];
            var edit = TextSplice.Between(previous, next);
            if (previous.Remove(edit.Start, edit.Removed).Insert(edit.Start, next.Substring(edit.Start, edit.Added)) != next)
                throw new InvalidOperationException("log splice roundtrip");
            previous = next;
        }
        Check(true, "2000 append, rollover, clear and Unicode log edits reproduce the complete text");
        var patch = TextSplice.Between("first\r\n", "first\r\nsecond\r\n");
        Check(patch.Removed == 0 && patch.Added == 8 && patch.Start == 7,
            "appending log output edits only the new characters");
        Check(patch.MapSelection(2) == 2 && patch.MapSelection(7) == 7,
            "appending output preserves the reader's existing selection");
        patch = TextSplice.Between("old text tail", "new tail");
        Check(patch.MapSelection(13) == 8 && patch.MapSelection(10) == 5,
            "replacement remaps selections after changed content");
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) _ = TextSplice.Between(previous, previous);
        Check(GC.GetAllocatedBytesForCurrentThread() == bytes, "unchanged log comparison allocates no managed bytes");
        var activity = new LogMemoryActivity();
        Check(!activity.TakeRequest(100, true), "an untouched log never requests repeated cleanup");
        activity.Changed(100);
        Check(!activity.TakeRequest(100.5, true) && !activity.TakeRequest(101, false),
            "log cleanup waits for scrolling to settle and interaction gates to clear");
        Check(activity.TakeRequest(101, true) && !activity.TakeRequest(102, true),
            "settled log activity requests exactly one cleanup without leaving the page");
        activity.Changed(102);
        Check(!activity.TakeRequest(103, true) && activity.TakeRequest(111, true),
            "frequent log interactions cannot request cleanup more than once per ten seconds");
        activity.Changed(120); activity.Changed(121); activity.Changed(122); activity.Changed(123);
        Check(activity.TakeRequest(123, true), "continuous log output does not postpone cleanup forever");
        activity.Changed(124); activity.Cancel();
        Check(!activity.TakeRequest(140, true), "leaving Log cancels its pending activity cleanup");
    }
}
