using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Rendering;
using Vortice.Mathematics;

namespace BloodborneLauncher.Native;
internal static class ResourceBudgetTests
{
    sealed class Resource : IDisposable
    {
        public bool Disposed;
        public void Dispose()
        {
            if (Disposed)
                throw new InvalidOperationException("double disposal");
            Disposed = true;
        }
    }

    public static void Run(string temporary)
    {
        void Check(bool yes, string message)
        {
            if (!yes)
                throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }

        var viewport = new Rect(0, 100, 800, 400);
        var rows = Enumerable.Range(0, 5000).Select(i => new Op(Ink.Text, new Rect(10, 100 + i * 32, 700, 24), new Color4(1, 1, 1, 1), "Row " + i, Clip: viewport, Scrollable: true)).ToArray();
        using var resources = new SceneResourceCache<TextKey, Resource>();
        for (int page = 0; page < 100; page++)
        {
            resources.BeginScene();
            foreach (var row in rows)
                if (ViewportResources.Keep(row, page * 960))
                    resources.Use(TextKey.From(row), _ => new Resource());
            resources.EndScene();
            if (resources.Count > 24)
                throw new InvalidOperationException("off-screen rows accumulated native layouts");
        }

        Check(resources.Count <= 24 && resources.Released > 1000, "scrolling 5000 rows retains only the viewport text budget and releases earlier rows");
        resources.Clear();
        Check(resources.Count == 0 && resources.Created == resources.Released, "leaving a large page releases every retained text-layout resource");
        var images = rows.Select((op, i) => op with { Kind = Ink.Image, Image = "trophy-" + i }).ToArray();
        using var bitmaps = new SceneResourceCache<string, Resource>();
        for (int offset = 0; offset <= 4000; offset += 400)
        {
            bitmaps.BeginScene();
            foreach (var image in images)
                if (ViewportResources.Visible(image, offset))
                    bitmaps.Use(image.Image, _ => new Resource());
            bitmaps.EndScene();
            if (bitmaps.Count > 16) throw new InvalidOperationException("offscreen bitmap retention exceeded viewport");
        }
        Check(bitmaps.Count <= 16 && bitmaps.Released > 100, "scrolling trophy images releases bitmap resources outside the viewport");
        bitmaps.Clear();
        Check(bitmaps.Created == bitmaps.Released, "all viewport image resources are released on scene exit");
        var clipped = new Op(Ink.Image, new Rect(0, 120, 50, 40), new Color4(1, 1, 1, 1), Clip: viewport, Scrollable: true, ContentClip: new Rect(600, 120, 50, 40));
        Check(!ViewportResources.Visible(clipped, 0), "images outside their local content clip do not stay resident");
        foreach (var size in new[] { (1280f, 800f), (1920f, 1080f), (3440f, 1440f), (900f, 900f) })
        {
            var crop = WallpaperFraming.Crop(1920, 1080, size.Item1, size.Item2);
            Check(crop.Left >= 0 && crop.Top >= 0 && crop.Right <= 1920.01f && crop.Bottom <= 1080.01f && Math.Abs(crop.Width / crop.Height - size.Item1 / size.Item2) < .001f, "wallpaper framing preserves aspect ratio and covers viewport " + size);
        }
        foreach (var size in new[] { (1280f,800f,220f), (1920f,1080f,300f), (3440f,1440f,330f), (900f,900f,220f) })
        {
            var book=DialogPlacement.Book(size.Item3,size.Item1,size.Item2);
            float center=book.X+book.Width/2;
            float centerY=book.Y+book.Height/2;
            var crop=WallpaperFraming.Crop(1920,1080,size.Item1,size.Item2,center,centerY);
            float mappedCenter=(960-crop.X)*size.Item1/crop.Width;
            float mappedCenterY=(540-crop.Y)*size.Item2/crop.Height;
            Check(Math.Abs(mappedCenter-center)<.01f && Math.Abs(mappedCenterY-centerY)<.01f && crop.Left>=-.01f && crop.Right<=1920.01f && crop.Top>=-.01f && crop.Bottom<=1080.01f,
                "wallpaper center follows the book without uncovered edges "+size);
        }
        var filter = new GameOutputFilter();
        filter.Reset(false);
        Check(!filter.Accept("Lantern Engine v0.9.37") && !filter.Accept("Graphics config: output_res=1920x1080") && !filter.Accept("Engine: Dream mirror prepared locally"), "combined logs hide launcher metadata and preparation messages");
        Check(!filter.Accept("Starting: 'C:/out/bb-probe.exe' 'boot-linked.bin'") && filter.Accept("Streaming: game files") && filter.Accept("Runtime: application heap API registered"), "the game-process boundary retains actual engine and game diagnostics");
        Check(!filter.Accept("bb-probe exited with code 0"), "the launcher's exit-summary wrapper does not enter the game log");
        filter.Reset(false);
        Check(filter.Accept("GPU: NVIDIA GeForce RTX 4090"), "standalone runtime diagnostics are recognized without launcher metadata");
        string? supplied = Environment.GetEnvironmentVariable("BB_NATIVE_TEST_SESSION");
        if (!string.IsNullOrEmpty(supplied))
        {
            string copy = Path.Combine(temporary, "last_session.log");
            File.Copy(supplied, copy, true);
            var output = new LiveOutputBuffer(gameOnly: true);
            while (output.Position < new FileInfo(copy).Length)
                output.Poll(copy);
            string text = output.Text();
            Check(!text.Contains("Graphics config:") && !text.Contains("Engine executable:") && !text.Contains("Lantern Engine v"), "the supplied session excludes launcher-only headers");
            Check(text.Contains("Runtime:") && text.Contains("GPU:"), "the supplied session preserves the real game diagnostics");
        }
    }
}
