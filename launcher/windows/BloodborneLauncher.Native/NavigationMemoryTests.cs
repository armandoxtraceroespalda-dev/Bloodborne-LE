using BloodborneLauncher.Native.Platform;

namespace BloodborneLauncher.Native;
internal static class NavigationMemoryTests
{
    public static void Run()
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        var queued = new Queue<Action>();
        int collected = 0, trimmed = 0;
        using var memory = new NavigationMemoryReclaimer(queued.Enqueue,
            () => collected++, () => { trimmed++; return true; });
        Check(!memory.TryStart(true), "memory reclamation does not run on idle ticks without navigation");
        memory.Request();
        Check(!memory.TryStart(false) && queued.Count == 0, "navigation cleanup waits for the new scene and transition to settle");
        Check(memory.TryStart(true) && queued.Count == 1 && collected == 0, "navigation queues cleanup instead of running collection in the UI callback");
        Check(!memory.TryStart(true), "an already queued reclamation pass cannot overlap another pass");
        queued.Dequeue()();
        Check(collected == 1 && trimmed == 1 && memory.Collections == 1 && memory.WorkingSetTrims == 1, "one completed navigation collects discarded objects then trims the resident working set");
        Check(!memory.TryStart(true), "completed navigation is not cleaned repeatedly by the animation timer");
        memory.Request(); memory.Request(); memory.Request();
        memory.TryStart(true); queued.Dequeue()();
        Check(collected == 2 && queued.Count == 0, "rapid pending requests coalesce into a single reclamation pass");
        memory.Request(); memory.TryStart(true);
        memory.CancelPending(); memory.Request();
        queued.Dequeue()();
        Check(collected == 2 && memory.TryStart(true), "a new navigation invalidates queued work and preserves its latest pending cleanup");
        queued.Dequeue()();
        Check(collected == 3 && trimmed == 3, "the final destination still receives its reclamation pass after cancellation");
        memory.Request(); memory.TryStart(true); memory.Dispose(); queued.Dequeue()();
        Check(collected == 3 && !memory.TryStart(true), "closing the launcher cancels queued memory work");

        using var failure = new NavigationMemoryReclaimer(queued.Enqueue,
            () => throw new InvalidOperationException("simulated collection failure"), () => true);
        failure.Request(); failure.TryStart(true); queued.Dequeue()();
        Check(failure.Failures == 1 && failure.Collections == 0, "a reclamation failure is contained without escaping to navigation");
        failure.Request();
        Check(failure.TryStart(true), "a failed reclamation releases its worker gate for the next navigation");
        queued.Dequeue()();
    }
}
