using System.Buffers.Binary;
using System.Text;
using BloodborneLauncher.Core;
using VGAudio.Codecs.Atrac9;

namespace BloodborneLauncher.Native.Platform;
/// <summary>Back cue extension of the production FSB5 decoder; VGAudio stays shared with WPF.
/// Reads only CURSOL_CANCEL from the player's own bank. No game audio is distributed.</summary>
internal static class NativeBackSound
{
    public static bool Ensure(string game, string gui, CancellationToken cancel)
    {
        string target = Path.Combine(gui, "sounds", "back.wav");
        if (Valid(target))
            return true;
        string folder = Path.Combine(game, "dvdroot_ps4", "sound");
        if (!Directory.Exists(folder))
            return false;
        foreach (string bank in Directory.EnumerateFiles(folder, "sprj_main*.fsb").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                byte[] wave = ExtractFromBank(bank, cancel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, wave);
                    if (!Valid(target))
                        File.Move(temporary, target, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }

                return true;
            }
            catch (Exception e)when (e is IOException or InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException or OverflowException)
            {
            }
        }

        return false;
    }

    static bool Valid(string path)
    {
        try
        {
            using var input = File.OpenRead(path);
            return input.Length <= 2 * 1024 * 1024 && MenuWave.TryDuration(input, out _);
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal static byte[] ExtractFromBank(string bank, CancellationToken cancel) => DecodeBank(bank, new[] { new KeyValuePair<string, string>("back.wav", "CURSOL_CANCEL") }, cancel)["back.wav"];
    private sealed record Entry(string Name, long Offset, int Samples, int Rate, int Channels, byte[]? Config);
    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4));
    private static Dictionary<string, byte[]> DecodeBank(string bank, KeyValuePair<string, string>[] wanted, CancellationToken cancel)
    {
        using var file = File.OpenRead(bank);
        Span<byte> header = stackalloc byte[60];
        file.ReadExactly(header);
        if (!header[..4].SequenceEqual("FSB5"u8) || U32(header, 4) != 1)
            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Expected an FSB5 v1 bank."));
        int count = checked((int)U32(header, 8)), headerSize = checked((int)U32(header, 12)), nameSize = checked((int)U32(header, 16));
        uint dataSize = U32(header, 20), codec = U32(header, 24);
        if (count is < 1 or > 65536 || headerSize < count * 8 || headerSize > 8 * 1024 * 1024 || nameSize < count * 4 || nameSize > 8 * 1024 * 1024)
            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid FSB5 table bounds."));
        long dataAt = 60L + headerSize + nameSize;
        if (dataAt + dataSize != file.Length)
            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated or inconsistent FSB5 data."));
        if (codec is not (2 or 13))
            throw new NotSupportedException(BloodborneLauncher.Core.UiText.T("Expected original ATRAC9 or PCM16 menu audio."));
        var table = new byte[headerSize];
        file.ReadExactly(table);
        var names = new byte[nameSize];
        file.ReadExactly(names);
        var entries = new Entry[count];
        int at = 0;
        int[] rates =
        {
            4000,
            8000,
            11000,
            11025,
            16000,
            22050,
            24000,
            32000,
            44100,
            48000,
            96000
        };
        for (int i = 0; i < count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            if (at > table.Length - 8)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated FSB5 sample header."));
            ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(at, 8));
            at += 8;
            int frequency = (int)((flags >> 1) & 15);
            int rate = frequency < rates.Length ? rates[frequency] : 0;
            int channels = new[]
            {
                1,
                2,
                6,
                8
            }[(int)((flags >> 5) & 3)];
            byte[]? config = null;
            bool more = (flags & 1) != 0;
            while (more)
            {
                if (at > table.Length - 4)
                    throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated FSB5 metadata."));
                uint extra = U32(table, at);
                at += 4;
                int size = (int)((extra >> 1) & 0xFFFFFF);
                more = (extra & 1) != 0;
                if (size > table.Length - at)
                    throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid FSB5 metadata size."));
                switch (extra >> 25)
                {
                    case 1 when size == 1:
                        channels = table[at];
                        break;
                    case 2 when size == 4:
                        rate = checked((int)U32(table, at));
                        break;
                    case 9 when size is 4 or 8:
                        int configAt = table[at] == 0xFE ? at : at + 4;
                        if (configAt > at + size - 4)
                            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid ATRAC9 metadata."));
                        config = table.AsSpan(configAt, 4).ToArray();
                        break;
                }

                at += size;
            }

            int nameAt = checked((int)U32(names, i * 4));
            if (nameAt < count * 4 || nameAt >= names.Length)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid FSB5 name offset."));
            int length = names.AsSpan(nameAt).IndexOf((byte)0);
            if (length is < 1 or > 512)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid FSB5 sample name."));
            string name = Encoding.UTF8.GetString(names, nameAt, length);
            long offset = (long)((flags >> 7) & 0x07FFFFFF) * 32;
            if (offset > dataSize || i > 0 && offset < entries[i - 1].Offset)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid FSB5 sample offset."));
            entries[i] = new Entry(name, offset, (int)(flags >> 34), rate, channels, config);
        }

        var clips = new Dictionary<string, byte[]>();
        foreach (var(filename, sample)in wanted)
        {
            cancel.ThrowIfCancellationRequested();
            int index = Array.FindIndex(entries, e => e.Name.Equals(sample, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.Format("Menu sample not found: {0}", sample));
            var entry = entries[index];
            long size = (index + 1 < count ? entries[index + 1].Offset : dataSize) - entry.Offset;
            if (entry.Channels is not (1 or 2) || entry.Rate is < 8000 or > 192000 || entry.Samples < 1 || entry.Samples > entry.Rate * 5 || size is < 1 or > 2 * 1024 * 1024)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.Format("Invalid menu sample bounds: {0}", sample));
            var encoded = new byte[(int)size];
            file.Position = dataAt + entry.Offset;
            file.ReadExactly(encoded);
            clips[filename] = DecodeWave(entry, encoded, codec, cancel);
        }

        return clips;
    }

    private static byte[] DecodeWave(Entry entry, byte[] encoded, uint codec, CancellationToken cancel)
    {
        int pcmSize = checked(entry.Samples * entry.Channels * 2);
        if (pcmSize > 2 * 1024 * 1024 - 44)
            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Menu PCM clip is too large."));
        using var output = new MemoryStream(44 + pcmSize);
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcmSize);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)entry.Channels);
        writer.Write(entry.Rate);
        writer.Write(entry.Rate * entry.Channels * 2);
        writer.Write((ushort)(entry.Channels * 2));
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(pcmSize);
        if (codec == 2)
        {
            if (encoded.Length < pcmSize)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated PCM menu sample."));
            writer.Write(encoded, 0, pcmSize);
        }
        else
        {
            if (entry.Config == null)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Missing ATRAC9 configuration."));
            var decoder = new Atrac9Decoder();
            decoder.Initialize(entry.Config);
            var cfg = decoder.Config;
            if (cfg.ChannelCount != entry.Channels || cfg.SampleRate != entry.Rate)
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("ATRAC9 format does not match its FSB header."));
            var superframe = new byte[cfg.SuperframeBytes];
            var pcm = Enumerable.Range(0, cfg.ChannelCount).Select(_ => new short[cfg.SuperframeSamples]).ToArray();
            int written = 0, position = 0;
            while (written < entry.Samples)
            {
                cancel.ThrowIfCancellationRequested();
                if (position > encoded.Length - superframe.Length)
                    throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated ATRAC9 menu sample."));
                Buffer.BlockCopy(encoded, position, superframe, 0, superframe.Length);
                position += superframe.Length;
                decoder.Decode(superframe, pcm);
                int count = Math.Min(cfg.SuperframeSamples, entry.Samples - written);
                for (int i = 0; i < count; i++)
                    for (int ch = 0; ch < entry.Channels; ch++)
                        writer.Write(pcm[ch][i]);
                written += count;
            }
        }

        var wave = output.ToArray();
        if (!MenuWave.IsPcm16(wave))
            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Decoded menu sound is invalid."));
        return wave;
    }
}
