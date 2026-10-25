using System.Runtime.InteropServices;
using System.Text;

namespace BloodborneLauncher.Native.Platform;
/// <summary>Win32 transport for the production Chime's two-note synthesis, without WPF MediaPlayer.</summary>
internal sealed class NativeTrophySound : IDisposable
{
    const int Rate = 44100;
    readonly string _alias = "bbtrophy" + Guid.NewGuid().ToString("N");
    string? _temporary;
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern uint mciSendString(string command, StringBuilder? answer, int length, nint callback);
    public NativeTrophySound(int volume, string custom)
    {
        string file = custom;
        try
        {
            if (file.Length == 0 || !File.Exists(file))
            {
                _temporary = Path.Combine(Path.GetTempPath(), _alias + ".wav");
                File.WriteAllBytes(_temporary, Wave(1));
                file = _temporary;
            }

            if (file.Contains('"'))
                return;
            if (mciSendString("open \"" + file + "\" alias " + _alias, null, 0, 0) != 0)
                return;
            mciSendString("setaudio " + _alias + " volume to " + Math.Clamp(volume * 10, 0, 1000), null, 0, 0);
            mciSendString("play " + _alias, null, 0, 0);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        mciSendString("close " + _alias, null, 0, 0);
        if (_temporary != null)
        {
            try
            {
                File.Delete(_temporary);
            }
            catch (IOException)
            {
            }

            _temporary = null;
        }
    }

    private static byte[] Wave(double volume)
    {
        var seconds = 1.6;
        var count = (int)(Rate * seconds);
        var samples = new double[count];
        Bell(samples, 0.00, 659.25, 0.55); // E5
        Bell(samples, 0.14, 987.77, 0.65); // B5
        var peak = samples.Max(Math.Abs);
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8);
        writer.Write(36 + count * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(count * 2);
        var gain = peak > 0 ? 0.8 * volume / peak : 0;
        foreach (var s in samples)
            writer.Write((short)Math.Clamp(s * gain * short.MaxValue, short.MinValue, short.MaxValue));
        writer.Flush();
        return memory.ToArray();
    }

    private static void Bell(double[] samples, double start, double frequency, double amplitude)
    {
        var partials = new (double Ratio, double Level, double Decay)[]
        {
            (1.0, 1.0, 2.6),
            (2.0, 0.35, 4.0),
            (2.76, 0.18, 6.0),
            (5.4, 0.06, 9.0),
        };
        var first = (int)(start * Rate);
        for (var i = first; i < samples.Length; i++)
        {
            var t = (i - first) / (double)Rate;
            var attack = Math.Min(1.0, t / 0.006);
            var value = 0.0;
            foreach (var(ratio, level, decay)in partials)
                value += level * Math.Exp(-decay * t) * Math.Sin(2 * Math.PI * frequency * ratio * t);
            samples[i] += amplitude * attack * value;
        }
    }
}
