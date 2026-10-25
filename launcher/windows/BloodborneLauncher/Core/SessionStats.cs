using System.Globalization;
using System.Text.RegularExpressions;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// One play session from the engine's output: how long it ran, the frame rate (from the
    /// "Frame stats:" lines, printed with Frame statistics on) and the stutters ("Stall:" lines).
    /// Playtime adds up per player profile in the settings (playtime.&lt;profile&gt;, seconds).
    /// </summary>
    public sealed class SessionStats
    {
        private const string RefusedText = "THD: the server refused this PC's sign-in as ";
        // Formatting saved playtime must not construct compiled parsers at startup.
        private static class Patterns
        {
            internal static readonly Regex FrameStats = new(@"^Frame stats: ([\d.]+) FPS, worst frame ([\d.]+) ms", RegexOptions.Compiled);
            internal static readonly Regex Stall = new(@"^Stall: ([\d.]+) ms frame", RegexOptions.Compiled);
            internal static readonly Regex Memory = new(@"^Memory: game (\d+) MiB in RAM", RegexOptions.Compiled);
            internal static readonly Regex Refused = new(@"refused this PC's sign-in as (.*?) \((.*)\): sign in again", RegexOptions.Compiled);
        }

        // Session summaries need aggregates, not an unbounded history of samples.
        private long _fpsCount;
        private double _fpsSum, _fpsLowest = double.PositiveInfinity;

        public DateTime Started { get; } = DateTime.Now;
        public TimeSpan Length { get; private set; }
        public double WorstFrameMs { get; private set; }
        public int Stutters { get; private set; }
        public double AverageFps => _fpsCount > 0 ? _fpsSum / _fpsCount : 0;
        public double LowestFps => _fpsCount > 0 ? _fpsLowest : 0;
        public bool HasFrameStats => _fpsCount > 0;
        /// <summary>The game's RAM in MiB, first and last of the once-a-minute lines (0: none).</summary>
        public long RamStartMiB { get; private set; }
        public long RamEndMiB { get; private set; }
        /// <summary>The Hunter's Dream refused this PC's sign-in during the session: the account name and
        /// the server's reason ("HTTP 401" when it gave none). Null when it did not.</summary>
        public (string Name, string Reason)? SignInRefused { get; private set; }

        public void Observe(string line)
        {
            if (line.StartsWith("Frame stats: ", StringComparison.Ordinal) && Patterns.FrameStats.Match(line) is { Success: true } m)
            {
                double fps = Parse(m.Groups[1].Value);
                _fpsCount++;
                _fpsSum += fps;
                _fpsLowest = Math.Min(_fpsLowest, fps);
                WorstFrameMs = Math.Max(WorstFrameMs, Parse(m.Groups[2].Value));
            }
            else if (line.StartsWith("Memory: game ", StringComparison.Ordinal) && Patterns.Memory.Match(line) is { Success: true } r)
            {
                RamEndMiB = long.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
                if (RamStartMiB == 0) RamStartMiB = RamEndMiB;
            }
            else if (line.StartsWith("Stall: ", StringComparison.Ordinal) && Patterns.Stall.Match(line) is { Success: true } s)
            {
                Stutters++;
                WorstFrameMs = Math.Max(WorstFrameMs, Parse(s.Groups[1].Value));
            }
            // Anywhere in the line, should a wrapper ever put something before the engine's "THD: ".
            else if (line.Contains(RefusedText, StringComparison.Ordinal))
            {
                var said = Patterns.Refused.Match(line);
                var reason = said.Success ? said.Groups[2].Value.Trim() : "";
                SignInRefused = (said.Success ? said.Groups[1].Value : "", reason.Length > 0 ? reason : "HTTP 401");
            }
        }

        public void Finish() => Length = DateTime.Now - Started;

        /// <summary>"1 h 12 min · 112 FPS average (lowest 98) · worst frame 107 ms · 3 stutters".</summary>
        public string Summary =>
            Duration(Length) + (HasFrameStats
                ? BloodborneLauncher.Core.UiText.F($" · {AverageFps:0} FPS average (lowest {LowestFps:0}) · worst frame {WorstFrameMs:0} ms · ") +
                  (Stutters == 1 ? "1 stutter" : $"{Stutters} stutters")
                : "") +
            (RamEndMiB > 0 ? $" · RAM {RamStartMiB / 1024.0:0.0} → {RamEndMiB / 1024.0:0.0} GB" : "");

        public static string Duration(TimeSpan time) =>
            time.TotalHours >= 1 ? BloodborneLauncher.Core.UiText.F($"{(int)time.TotalHours} h {time.Minutes} min") :
            time.TotalMinutes >= 1 ? BloodborneLauncher.Core.UiText.F($"{(int)time.TotalMinutes} min") : BloodborneLauncher.Core.UiText.F($"{Math.Max(0, (int)time.TotalSeconds)} s");

        public static TimeSpan Playtime(LauncherSettings s, string profile) =>
            TimeSpan.FromSeconds(Math.Max(0, s.Int(Key(profile))));

        public static void AddPlaytime(LauncherSettings s, string profile, TimeSpan time) =>
            s.Set(Key(profile), (int)Math.Min(int.MaxValue, Playtime(s, profile).TotalSeconds + time.TotalSeconds));

        private static string Key(string profile) => "playtime." + profile.ToLowerInvariant();

        private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    }
}
