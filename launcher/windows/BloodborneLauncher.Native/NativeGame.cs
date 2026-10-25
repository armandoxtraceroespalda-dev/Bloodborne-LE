using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    const uint GameEventsMessage = 0x8003;
    bool _closeRequested;
    void WakeGameEvents()
    {
        if (!_closed && _window != 0) W.PostMessage(_window, GameEventsMessage, 0, 0);
    }
    void StartOrStopGame()
    {
        if (_busy) return;
        if (_game?.Running == true)
        {
            Background(async () => { await _game.StopAsync(); return () => { PollGameEvents(); Rebuild(); }; });
            return;
        }
        SdlPad.Stop();
        var leftovers = System.Diagnostics.Process.GetProcessesByName("bb-probe");
        try
        {
            if (leftovers.Length > 0)
                throw new InvalidOperationException(UiText.T("The game is still running in the background. Close it before starting another session."));
        }
        finally { foreach (var process in leftovers) process.Dispose(); }
        _profile.ApplyToGame();
        var settings = _profile.SourceSettings();
        if (!GameRunner.Custom(settings) && !Paths.IsPortDir(Paths.PortDir))
            throw new InvalidOperationException(UiText.T("The engine folder is not set. Choose it in Game."));
        if (!PreflightGame(settings)) return;
        _profile.ApplyToGame();
        if (_game == null)
        {
            _game = new NativeGameSession();
            _game.EventsAvailable += WakeGameEvents;
        }
        _output.BeginSession(SessionOutputCandidates());
        StopLauncherPad();
        string? error = _game.Start(_profile);
        if (error != null) throw new InvalidOperationException(error);
        // This runner owns a freshly recreated last_run.log; read its first line.
        _output.BeginSession(Array.Empty<string>());
        if (settings.Bool("safe_mode_next")) _profile.Set("safe_mode_next", "false", true);
        if (settings.Bool("section_stats")) _profile.Set("section_stats", "false", true);
        if (_profile.Dirty) _profile.ApplyToGame();
        _notice = "";
        if (_directPlay) W.ShowWindow(_window, 6);
        if (_profile.Str("switch_to_output", "true") == "true")
        {
            _moreTab = 1;
            _subpage = "";
            _outputOffset = 0;
            _followOutput = _outputFollowPending = true;
            _nextOutputPoll = 0;
            Switch(8);
        }
        PrepareBackgroundLog();
        UpdateBackgroundLogTimer();
        Rebuild();
    }

    bool PreflightGame(LauncherSettings settings)
    {
        _profile.Set("online_pool", settings.HuntersDreamToken.Length > 0 && settings.Str("hd_name").Length > 0 ? "huntersdream" : "");
        if (!HuntersDream.OnlinePaused && _profile.Ini.Get("online_enabled") == "1" && _profile.Ini.Get("online_pool") != "huntersdream")
        {
            string? problem = _profile.Ini.Get("online_username").Contains('@') ? "The account name is the name registered on the server, not an email address."
                : _profile.Ini.Get("online_username").Trim().Length == 0 ? "Enter the account name before playing online."
                : settings.OnlinePassword.Length == 0 ? "Enter the account password before playing online." : null;
            if (problem != null) throw new InvalidOperationException(UiText.T(problem));
        }
        GameEnvironment.RegionPatchOnline = null;
        GameEnvironment.ResolutionPatchChoice = null;
        if (!HuntersDream.OnlinePaused && _profile.Ini.Get("online_enabled") == "1" &&
            Patches.RegionChanging(GameEnvironment.PatchesDir(settings)) is { Count: > 0 })
        {
            int result = ShowMessage(UiText.T("Leave region-changing patches out for this online session?"), UiText.T("Play online"), 0x23);
            if (result == 2) return false;
            GameEnvironment.RegionPatchOnline = result == 6 ? "skip" : "keep";
        }
        var (width, height) = Choices.OutputSize(_profile.Ini.Get("output_res", "1920x1080"));
        if (Patches.ResolutionSetting(GameEnvironment.PatchesDir(settings)) is { Count: > 0 } resolutions)
        {
            string names = string.Join(", ", resolutions.Select(patch => "\"" + patch.Name + "\""));
            if ((width, height) == (1920, 1080) && settings.Str("resolution_patch_native") != names)
            {
                var match = resolutions.Select(patch => System.Text.RegularExpressions.Regex.Match(patch.Name, @"(\d{3,5})x(\d{3,5})")).FirstOrDefault(value => value.Success);
                if (match != null)
                {
                    int result = ShowMessage(UiText.T("Use the resolution patch's size as the output resolution so upscaler presets work normally?"), UiText.T("Output resolution"), 0x23);
                    if (result == 2) return false;
                    if (result == 6)
                    {
                        _profile.Set("output_res", match.Groups[1].Value + "x" + match.Groups[2].Value);
                        GameEnvironment.ResolutionPatchChoice = "skip";
                    }
                    else _profile.Set("resolution_patch_native", names, true);
                }
            }
            else if ((width, height) != (1920, 1080))
            {
                if (settings.Str("resolution_patch_always") == names) GameEnvironment.ResolutionPatchChoice = "keep";
                else
                {
                    int result = ShowMessage(UiText.T("Leave resolution patches out and use the selected output resolution?"), UiText.T("Output resolution"), 0x23);
                    if (result == 2) return false;
                    GameEnvironment.ResolutionPatchChoice = result == 6 ? "skip" : "keep";
                }
            }
        }
        if (OperatingSystem.IsWindows() && !settings.Bool("memory_check_off") && MemoryCheck.Problem(_profile.Ini) is { } memory && !Confirm(memory))
            return false;
        return true;
    }

    void PollGameEvents()
    {
        if (_closed || _game == null) return;
        while (_game.TryRead(out var entry) && entry != null)
        {
            if (entry.Kind is "trophy" or "cheater")
            {
                var settings = _game.Settings!;
                if (entry.Kind == "cheater" || settings.Bool("trophy_notifications"))
                {
                    TrophyTracker.ById.TryGetValue(entry.Value, out var trophy);
                    QueueTrophy(settings, trophy, entry.Kind == "cheater");
                }
            }
            else if (entry.Kind == "error") _notice = entry.Text;
            else if (entry.Kind == "exit")
            {
                // Drain the final flushed log even if the background timer stops at exit.
                _nextOutputPoll = 0;
                PollReferenceOutput(_clock.Elapsed.TotalSeconds);
                UpdateBackgroundLogTimer();
                if (entry.Session is { } session)
                {
                    if (session.Length > TimeSpan.FromSeconds(10))
                        _profile.EditSource((settings, ini) => SessionStats.AddPlaytime(settings, entry.Profile, session.Length));
                    if (session.SignInRefused is { } refused) _profile.Set("hd_refused", refused.Reason, true);
                    _profile.ApplyToGame();
                }
                if (_directPlay)
                {
                    _directPlay = false;
                    if (entry.Value == 0) { W.DestroyWindow(_window); return; }
                    W.ShowWindow(_window, 9);
                }
                if (entry.Value != 0) _notice = UiText.F($"The game exited with code {entry.Value}.");
                Rebuild();
            }
        }
    }

    void RequestClose()
    {
        if (_closeRequested) return;
        if (_game?.Running != true) { W.DestroyWindow(_window); return; }
        if (_busy || !Confirm(UiText.T("Stop the game and close the launcher?"))) return;
        _closeRequested = true;
        Background(async () =>
        {
            await _game.StopAsync();
            return () => { PollGameEvents(); W.DestroyWindow(_window); };
        });
    }
}
