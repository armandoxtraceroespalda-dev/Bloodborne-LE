using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Interaction;

namespace BloodborneLauncher.Native;
internal static class NoticeQueueTests
{
    public static void Run()
    {
        static void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }
        var queue = new NoticeQueue<int>(64);
        Check(TrophyTracker.All.Length + 2 <= 64, "notification capacity holds the entire trophy catalog plus synthetic notices");
        Check(!queue.TryTake(out _) && queue.Count == 0, "empty notification queue does not produce a phantom banner");
        bool accepted = true;
        for (int i = 0; i < 64; i++) accepted &= queue.Enqueue(i);
        Check(accepted && queue.Count == 64, "a burst of 64 notifications is retained without creating presentation resources");
        Check(!queue.Enqueue(1000) && queue.Count == 64, "queue overflow is bounded and does not evict an earlier notice");
        bool ordered = true;
        for (int i = 0; i < 64; i++) ordered &= queue.TryTake(out int next) && next == i;
        Check(ordered && queue.Count == 0, "burst notifications drain in arrival order, one at a time");
        queue.Enqueue(42);
        Check(queue.Contains(i => i == 42) && !queue.Contains(i => i == 43), "pending preview checks do not dequeue or reorder notices");
        queue.Clear();
        Check(queue.Count == 0 && !queue.TryTake(out _), "queue disposal releases pending references and leaves no stale notification");
        Check(queue.Enqueue(99) && queue.TryTake(out int reused) && reused == 99, "queue storage can be reused after clearing");
        bool rejected = false;
        try { _ = new NoticeQueue<int>(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid queue capacities are rejected");
    }
}
