using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class GameSessionTests
{
    public static void Run(string temporary)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        if (OperatingSystem.IsWindows()) return; // Unix fixture process; never launches the actual game.
        string? user32 = Environment.GetEnvironmentVariable("BB_NATIVE_TEST_USER32");
        if (string.IsNullOrEmpty(user32))
        {
            Console.WriteLine("SKIP direct-process fixture: set BB_NATIVE_TEST_USER32 to the compiled monitor stub.");
            return;
        }
        NativeLibrary.SetDllImportResolver(typeof(GameSessionTests).Assembly,
            (name, assembly, searchPath) => name == "user32.dll" ? NativeLibrary.Load(user32) : 0);
        string root = Path.Combine(temporary, "direct-game"), user = Path.Combine(root, "user");
        Directory.CreateDirectory(root);
        string script = Path.Combine(root, "fake game.sh");
        File.WriteAllText(script, "#!/bin/sh\n" +
            "printf 'Runtime: native fixture config=%s gpu=%s text=%s voice=%s\\n' \"$BB_CONFIG\" \"$BB_GPU_ID\" \"$BB_TEXT_LANGUAGE\" \"$BB_VOICE_LANGUAGE\"\n" +
            "printf 'Runtime: trophy 14 unlocked\\n'\n" +
            "printf 'GPU: window and Vulkan presenter ready\\n'\n" +
            "sleep 0.2\nexit 0\n");
        string config = Path.Combine(root, "settings.json"), ini = Path.Combine(root, "lantern.ini");
        File.WriteAllText(config, new JsonObject
        {
            ["port_dir"] = root, ["game_dir"] = root, ["user_dir"] = user,
            ["launch_method"] = "custom", ["custom_command"] = "/bin/sh",
            ["custom_args"] = "\"" + script + "\"", ["save_backups"] = false,
            ["text_language"] = "20", ["language"] = "3"
        }.ToJsonString());
        File.WriteAllText(ini, "output_res=2560x1440\nupscaler=dlss\ngpu_id=-1\ncheat_stamina=1\n");
        var profile = new ProfileSession(new[] { "--config", config, "--ini", ini, "--assets", root, "--profile", Path.Combine(root, "staged") });
        string staged = Paths.IniPath;
        using var session = new NativeGameSession();
        using var available = new ManualResetEventSlim();
        int wakeups = 0;
        session.EventsAvailable += () =>
        {
            Interlocked.Increment(ref wakeups);
            try { available.Set(); } catch (ObjectDisposedException) { /* The fixture consumer has exited. */ }
        };
        string? launchError = session.Start(profile);
        Check(launchError == null, "the source-linked game runner starts a fixture process without another launcher executable: " + launchError);
        Check(Paths.IniPath == staged && ReferenceEquals(Workspace.Current.Ini, profile.Ini), "launch preparation restores the UI's isolated paths and INI instance");
        var events = new List<NativeGameSession.Event>();
        Check(available.Wait(TimeSpan.FromSeconds(5)) && Volatile.Read(ref wakeups) > 0,
            "game output signals its consumer without a render tick or launcher click");
        Check(session.TryRead(out var first) && first is { Kind: "trophy", Value: 14 },
            "the trophy event is queued before its background wake signal is delivered");
        if (first != null) events.Add(first);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            while (session.TryRead(out var entry)) if (entry != null) events.Add(entry);
            if (events.Any(e => e.Kind == "exit")) break;
            Thread.Sleep(10);
        }
        Check(events.Any(e => e.Kind == "exit" && e.Value == 0), "direct game lifecycle reports the fixture exit code");
        string output = File.ReadAllText(Path.Combine(user, "last_run.log"));
        Check(output.Contains(ini) && !output.Contains(profile.SandboxIni), "the game receives the real merged INI rather than the preview's staged file");
        Check(output.Contains("text=20 voice=3"), "direct launch preserves separate game text and voice languages");
        Check(!output.Contains("Graphics config:") && !output.Contains("Engine executable:"), "the game-output stream excludes launcher preparation metadata");
        Check(events.Count(e => e.Kind == "cheater") == 1 && events.Any(e => e.Kind == "trophy" && e.Value == 14), "direct launch retains cheat notification and real trophy events");
        Check(File.ReadAllText(Path.Combine(user, TrophyTracker.LogName)).Contains("14 "), "real trophy progress is persisted separately from the synthetic cheat notice");
        UiText.UseTextLanguage("1");
    }
}
