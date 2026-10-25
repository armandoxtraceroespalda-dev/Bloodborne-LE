using System.Buffers.Binary;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class PageMigrationTests
{
    public static void Run(ProfileSession profile, string temporary)
    {
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }

        // Exercise the real, source-linked TRP extractor with a local synthetic game dump.
        // A trophy page must extract a picture, without ever rewriting the player's dump.
        byte[] picture = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhAoAAAAASUVORK5CYII=");
        byte[] trp = new byte[0x60 + 64 + picture.Length];
        BinaryPrimitives.WriteUInt32BigEndian(trp, 0xDCA24D00);
        BinaryPrimitives.WriteUInt32BigEndian(trp.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32BigEndian(trp.AsSpan(20), 64);
        "TROP000.PNG"u8.CopyTo(trp.AsSpan(0x60));
        BinaryPrimitives.WriteUInt64BigEndian(trp.AsSpan(0x60 + 32), 0xA0);
        BinaryPrimitives.WriteUInt64BigEndian(trp.AsSpan(0x60 + 40), (ulong)picture.Length);
        picture.CopyTo(trp.AsSpan(0xA0));
        string game = Path.Combine(temporary, "icon-dump"), file = Path.Combine(game, "sce_sys", "trophy", "trophy00.trp");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, trp);
        var settings = profile.SourceSettings();
        settings.Set("game_dir", game);
        string? extracted = TrophyIcons.For(settings, 0);
        Check(extracted != null && File.ReadAllBytes(extracted).SequenceEqual(picture), "native achievements resolves the game's actual TRP picture");
        Check(File.ReadAllBytes(file).SequenceEqual(trp), "trophy artwork extraction leaves the source game archive intact");
        Check(TrophyIcons.For(settings, 39) == null, "a missing trophy picture falls back without manufacturing an icon");
        var tracker = new TrophyTracker();
        tracker.SetUserDir(Path.Combine(temporary, "trophy-user"));
        Directory.CreateDirectory(Path.GetDirectoryName(tracker.LogPath)!);
        // The notification preview must not be represented by a real unlock record.
        var before = tracker.Count;
        tracker.Poll(false);
        Check(tracker.Count == before && !File.Exists(tracker.LogPath), "an empty achievement view never invents unlocks or creates a trophy log");
    }
}
