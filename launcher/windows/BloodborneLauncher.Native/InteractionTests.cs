using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;

namespace BloodborneLauncher.Native;
internal static class InteractionTests
{
    public static void Run(string temporary)
    {
        void Check(bool yes, string name)
        {
            if (!yes)
                throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }

        const uint api = (1u << 22) | (3u << 12);
        var a = new GpuAdapter(5, "NVIDIA GeForce RTX 3090", "NVIDIA", 24L << 30, 2, api);
        var b = new GpuAdapter(2, "NVIDIA GeForce RTX 4090", "NVIDIA", 24L << 30, 2, api);
        Check(GpuSelection.Best(new[] { a, b }) == b, "recommendations prefer RTX 4090 over an equal-memory RTX 3090");
        Check(GpuSelection.Best(new[] { b, a }) == b && b.Index == 2, "GPU ranking preserves Vulkan IDs and does not depend on enumeration order");
        var integrated = new GpuAdapter(0, "AMD Radeon Graphics", "AMD", 32L << 30, 1, api);
        Check(GpuSelection.Best(new[] { integrated, a, b }) == b, "shared memory cannot make an integrated GPU win over the discrete RTX card");
        Check(GpuSelection.Best(Array.Empty<GpuAdapter>()) == null, "recommendations tolerate Vulkan enumeration being unavailable");
        var buffer = new LiveOutputBuffer();
        string path = Path.Combine(temporary, "cached-output.log");
        File.WriteAllText(path, "selectable\n");
        buffer.Poll(path);
        Check(ReferenceEquals(buffer.Text(), buffer.Text()), "scrolling or selecting output reuses its cached text snapshot");
        string old = buffer.Text();
        File.AppendAllText(path, "new line\n");
        buffer.Poll(path);
        Check(buffer.Text() != old && buffer.Text().Contains("new line"), "new output invalidates the snapshot without losing existing lines");
        string? bank = Environment.GetEnvironmentVariable("BB_NATIVE_TEST_FSB");
        if (!string.IsNullOrEmpty(bank))
        {
            byte[] sound = NativeBackSound.ExtractFromBank(bank, CancellationToken.None);
            Check(MenuWave.TryDuration(sound, out int duration) && duration > 0 && duration <= 5000, "CURSOL_CANCEL decodes from the supplied original Bloodborne bank");
        }
    }
}
