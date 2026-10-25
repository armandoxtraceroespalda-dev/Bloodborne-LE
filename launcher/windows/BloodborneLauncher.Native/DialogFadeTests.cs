using BloodborneLauncher.Native.Interaction;

namespace BloodborneLauncher.Native;
internal static class DialogFadeTests
{
    public static void Run()
    {
        static void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            Console.WriteLine("PASS " + label);
        }
        var fade = new DialogFade();
        fade.Open(10);
        Check(fade.Animating && fade.Value(10) == 0 && Math.Abs(fade.Value(10.09) - .5f) < .0001,
            "modal opens smoothly from transparent through half opacity");
        Check(!fade.Complete(10.1) && fade.Complete(10.181) && !fade.Animating && fade.Value(11) == 1,
            "opening ends once at full opacity and releases its animation clock");
        fade.Close(11);
        Check(fade.Value(11) == 1 && Math.Abs(fade.Value(11.09) - .5f) < .0001 && fade.Value(11.2) == 0,
            "closing fades the retained modal to transparent before teardown");
        Check(fade.Complete(11.2) && !fade.Complete(11.3) && fade.Value(12) == 0,
            "close completion is single-shot and stays transparent");
        fade.Open(20);
        float interrupted = fade.Value(20.04);
        fade.Close(20.04);
        Check(Math.Abs(fade.Value(20.04) - interrupted) < .0001 && fade.Value(20.13) < interrupted,
            "closing during fade-in starts at the current opacity without a jump");
        fade.Open(30);
        for (int i = 0; i < 10000; i++) _ = fade.Value(30.05);
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        for (int i = 0; i < 10000; i++) sum += fade.Value(30.05);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(sum > 0 && allocated == 0, "modal opacity sampling makes no per-frame managed allocations");
    }
}
