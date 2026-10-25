using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class StartupSummaryTests
{
    public static void Run(ProfileSession profile)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            Console.WriteLine("PASS " + message);
        }
        Check(!profile.HelpLoaded, "creating the startup profile does not load other pages' help catalog");
        var help = profile.Help;
        Check(profile.HelpLoaded && ReferenceEquals(help, profile.Help), "help is loaded once when a settings page requests it");
        foreach (var choice in Choices.Upscalers)
            Check(StartupSummary.Upscaler(choice.Value) == choice.Label.Split(" (")[0], "Start upscaler caption matches the upstream selector: " + choice.Value);
        foreach (var choice in Choices.FpsModes)
            Check(StartupSummary.FrameRate(choice.Value) == choice.Label.Split(" (")[0], "Start frame-rate caption matches the upstream selector: " + choice.Value);
        foreach (string value in new[] { "1280x720", "2560x1440", "3440x1440", "invalid" })
            Check(StartupSummary.OutputSize(value) == Choices.OutputSize(value), "Start resolution parser preserves the upstream saved-value behavior: " + value);
        Check(StartupSummary.OutputSize("auto", 2561, 1441) == (2560, 1440), "Start automatic resolution uses primary-screen dimensions without a hardware recommendation scan");
        Check(StartupSummary.OutputSize("auto", 0, 0) == (1920, 1080), "Start automatic resolution preserves the upstream safe fallback");
        Check(StartupSummary.Upscaler("future_mode") == "future_mode", "unknown future upscaler values remain visible instead of being silently replaced");
    }
}
