using System.Text.Json.Nodes;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Interaction;
using BloodborneLauncher.Native.Rendering;

namespace BloodborneLauncher.Native;
internal static class SelfTest
{
    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "native-preview-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var previous = Paths.IniPath;
        try
        {
            void Check(bool ok, string name)
            {
                if (!ok)
                    throw new InvalidOperationException(name);
                Console.WriteLine("PASS " + name);
            }

            string ini = Path.Combine(dir, "lantern.ini"), config = Path.Combine(dir, "settings.json");
            File.WriteAllText(ini, "# Keep this comment\noutput_res=1920x1080\nupscaler=fsr4\nfuture_key=keep\n");
            File.WriteAllText(config, "{\"port_dir\":\"\",\"text_language\":\"1\",\"future_setting\":{\"a\":1},\"fps_mode\":\"60\"}");
            var arguments = new[]
            {
                "--config",
                config,
                "--ini",
                ini,
                "--assets",
                dir,
                "--profile",
                Path.Combine(dir, "preview")
            };
            var profile = new ProfileSession(arguments);
            StartupSummaryTests.Run(profile);
            profile.Set("output_res", "2560x1440");
            profile.Set("fps_mode", "90", true);
            profile.SavePreview();
            Check(File.ReadAllText(ini).Contains("output_res=1920x1080"), "preview writes remain isolated");
            var resumed = new ProfileSession(arguments);
            Check(resumed.Ini.Get("output_res") == "2560x1440" && resumed.Dirty, "saved preview resumes without applying to game");
            File.AppendAllText(ini, "game_changed=preserve\n");
            var concurrent = JsonNode.Parse(File.ReadAllText(config))!;
            concurrent["online_token"] = "new-token";
            File.WriteAllText(config, concurrent.ToJsonString());
            profile.ApplyToGame();
            var output = File.ReadAllText(ini);
            Check(output.Contains("output_res=2560x1440") && output.Contains("future_key=keep") && output.Contains("game_changed=preserve") && output.Contains("# Keep this comment"), "upstream INI merge keeps unknown and concurrent game values");
            var json = JsonNode.Parse(File.ReadAllText(config))!;
            Check(json["fps_mode"]!.ToString() == "90" && json["online_token"]!.ToString() == "new-token" && json["future_setting"]!["a"]!.ToString() == "1", "JSON apply merges only dirty settings");
            Check(!profile.Dirty, "apply clears staged changes");
            var schema = GraphicsSchema.Build();
            Check(schema.Count > 60 && schema.Select(o => o.Key).Distinct().Count() == schema.Count, "graphics schema has unique setting keys");
            Check(schema.Where(o => o.Kind == OptionKind.Slider).All(o => o.Max > o.Min && o.Step > 0), "slider ranges are valid");
            Check(Choices.Upscalers.Any(c => c.Value == "dlss") && Choices.Presets.Any(c => c.Value == "5"), "upstream choice contracts resolve");
            profile.Set("upscaler", "dlss");
            profile.Set("preset", "5");
            Check(schema.Where(o => o.Visible == "custom").All(o => GraphicsSchema.Visible(o, profile)), "custom scale follows upstream layout");
            profile.Set("upscaler", "off");
            Check(!schema.Where(o => o.Visible == "custom").Any(o => GraphicsSchema.Visible(o, profile)), "custom scale hidden with upscaler off");
            profile.Set("hdr", "0");
            Check(!schema.Where(o => o.Gate == "hdr").Any(o => GraphicsSchema.Enabled(o, profile)), "HDR children follow parent toggle");
            var refs = typeof(Program).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Check(!refs.Any(n => n is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Windows.Forms"), "native assembly has no WPF/WinForms dependency");
            var movementRows = PageCatalog.Build(3, profile);
            Check(movementRows.Single(o => o.Key == "stick_turn_rate").Default == "800", "keyboard turning speed defaults to 800 degrees per second");
            Check(!movementRows.Any(o => o.Key == "stick_release_delay_ms"), "retired key release delay is absent from launcher controls");
            var sectionIcons = new[] { "Screen", "Game effects", "Picture", "Upscaler", "HDR", "Info" }.Select(GraphicsSectionIcons.For).ToArray();
            Check(sectionIcons.All(icon => icon.Length == 1) && sectionIcons.Distinct().Count() == 6, "six graphics sections retain distinct monochrome icons; Experimental is undecorated");
            Check(GraphicsSectionIcons.For("Language") == "", "graphics icon selection leaves unrelated sections unchanged");
            Check(GraphicsSectionIcons.For("Experimental") == "", "Experimental deliberately has no heading icon");
            DialogSurfaceTests.Run();
            DialogFadeTests.Run();
            NoticeQueueTests.Run();
            AchievementLayoutTests.Run();
            PreferredGamepadTests.Run(dir);
            ControlDefaultsTests.Run(dir);
            SectionIconTests.Run();
            TestHoverResources();
            ScrollTests.Run(profile);
            ReferenceLayoutTests.Run();
            NavigationMemoryTests.Run();
            LogMemoryTests.Run();
            OptimizationTests.Run(dir);
            PageMigrationTests.Run(profile, dir);
            LiveOutputTests.Run(dir);
            InteractionTests.Run(dir);
            ResourceBudgetTests.Run(dir);
            GameSessionTests.Run(dir);
            EnginePresetTests.Run(dir);
            SharpUiTests.Run(dir);
            LauncherSearchPerformanceTests.Run(dir);
            FirstRunAssetTests.Run(dir);
            LocalEngineTests.Run(dir);
            Console.WriteLine("Native preview contracts passed.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
        finally
        {
            Paths.IniPath = previous;
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }

    private sealed class TestLayout : IDisposable
    {
        private readonly Action _release;
        public bool Disposed { get; private set; }

        public TestLayout(Action release) => _release = release;
        public void Dispose()
        {
            if (Disposed)
                throw new InvalidOperationException("Layout released twice");
            Disposed = true;
            _release();
        }
    }

    private static void TestHoverResources()
    {
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }

        int created = 0, released = 0;
        Func<string, TestLayout> factory = _ =>
        {
            created++;
            return new TestLayout(() => released++);
        };
        using var layouts = new SceneResourceCache<string, TestLayout>();
        layouts.BeginScene();
        var first = layouts.Use("label A", factory);
        layouts.Use("label B", factory);
        layouts.EndScene();
        var pointer = new PointerState();
        for (int i = 0; i < 10000; i++)
            pointer.Move(i % 10, i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
            pointer.Move(i % 10, i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "100000 pointer changes allocate no managed bytes");
        Check(created == 2 && released == 0 && layouts.Count == 2, "hover does not create or release scene layouts");
        for (int i = 0; i < 100; i++)
        {
            layouts.BeginScene();
            layouts.Use("label A", factory);
            layouts.Use("label B", factory);
            layouts.EndScene();
        }

        Check(created == 2 && ReferenceEquals(layouts.Get("label A"), first), "unchanged text reuses its existing layout");
        layouts.BeginScene();
        layouts.Use("label B", factory);
        layouts.Use("tooltip", factory);
        layouts.EndScene();
        Check(first.Disposed && created == 3 && released == 1 && layouts.Count == 2, "stale text layout released when scene membership changes");
        layouts.BeginScene();
        layouts.Use("label B", factory);
        layouts.EndScene();
        Check(layouts.Count == 1 && released == 2, "hiding tooltip releases only its own layout");
        layouts.Clear();
        Check(layouts.Count == 0 && released == created && layouts.Created == layouts.Released, "resource cache balances every creation and release");
        layouts.Clear();
        Check(released == created, "clearing cache twice never double releases");
        Check(pointer.Move(5, 100001) && !pointer.Move(5, 100002), "pointer movement within the same control does not change hover state");
        var glow = new Op(Ink.Glow, default, default, Trigger: 4);
        Check(glow.VisibleFor(4, -1) && !glow.VisibleFor(3, -1) && glow.VisibleFor(-1, 4), "cached highlight follows hover and keyboard focus");
        Check((glow with { Selected = true }).VisibleFor(-1, -1), "selected glow keeps pulsing without a scene rebuild");
    }
}
