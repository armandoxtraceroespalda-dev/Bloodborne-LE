using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal static class AchievementLayoutTests
{
    public static void Run()
    {
        static void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            Console.WriteLine("PASS " + label);
        }
        byte[] pixels = { 0, 0, 255, 255, 0, 128, 0, 128, 0, 0, 0, 0, 40, 70, 90, 128 };
        byte[] original = (byte[])pixels.Clone();
        ImagePixels.Grayscale(pixels);
        bool gray = true, alpha = true, premultiplied = true;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            gray &= pixels[i] == pixels[i + 1] && pixels[i] == pixels[i + 2];
            alpha &= pixels[i + 3] == original[i + 3];
            premultiplied &= pixels[i] <= pixels[i + 3];
        }
        Check(gray && pixels[0] == 54, "locked icon conversion uses neutral luminance for every RGB channel");
        Check(alpha && premultiplied && pixels[8] == 0, "locked icon conversion preserves transparency and premultiplied edges");
        byte[] once = (byte[])pixels.Clone();
        ImagePixels.Grayscale(pixels);
        Check(pixels.SequenceEqual(once), "grayscale conversion is stable on already gray icons");
        bool rejected = false;
        try { ImagePixels.Grayscale(new byte[3]); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "partial icon pixels are rejected instead of corrupting alpha");
        bool centered = true;
        foreach (var size in new[] { (1024f,720f,180f), (1280f,800f,210f), (1920f,1080f,260f), (2560f,1440f,300f) })
        {
            var book = DialogPlacement.Book(size.Item3, size.Item1, size.Item2);
            var box = DialogPlacement.Center(book, Math.Min(640, book.Width - 48), Math.Min(300, book.Height - 48));
            centered &= Math.Abs(box.Center.X - book.Center.X) < .01 && Math.Abs(box.Center.Y - book.Center.Y) < .01 &&
                box.Left >= book.Left && box.Right <= book.Right && box.Top >= book.Top && box.Bottom <= book.Bottom;
        }
        Check(centered, "dialogs stay centered inside the book at windowed and maximized sizes");
        var area = DialogPlacement.Book(240, 1600, 900);
        Check(area.X == 232 && area.Y == 38 && area.Right == 1588 && area.Bottom == 900,
            "dialog placement matches the rendered book and excludes the sidebar");
        var ordinary = new Op(Ink.Text, new Rect(0,0,48,48), new Color4(1,1,1,1), "🍼", 34);
        Check(TextKey.From(ordinary) != TextKey.From(ordinary with { Emoji = true }),
            "emoji and menu text have separate layout keys so fonts cannot leak between them");
        bool localized = true;
        foreach (string language in new[] { "3", "20", "2", "4", "5", "17", "8", "0" })
        {
            BloodborneLauncher.Core.UiText.UseTextLanguage(language);
            localized &= BloodborneLauncher.Core.UiText.T("All achievements") != "All achievements" &&
                BloodborneLauncher.Core.UiText.T("Cheater!") != "Cheater!" &&
                BloodborneLauncher.Core.UiText.T("I can't git gud...") != "I can't git gud..." &&
                !BloodborneLauncher.Core.UiText.Format("{0} of {1} points", 1, 2).Contains('(');
        }
        BloodborneLauncher.Core.UiText.UseTextLanguage("1");
        Check(localized, "achievement names and shortened point counts use all supported launcher languages");

    }
}
