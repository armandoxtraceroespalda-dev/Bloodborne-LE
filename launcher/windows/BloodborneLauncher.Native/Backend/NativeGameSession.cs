using System.Collections.Concurrent;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;

/// <summary>Own the source-linked game runner without launching another UI process.</summary>
internal sealed class NativeGameSession : IDisposable
{
    public sealed record Event(string Kind, int Value = 0, string Text = "", SessionStats? Session = null, string Profile = "");
    readonly GameRunner _runner = new();
    readonly ConcurrentQueue<Event> _events = new();
    readonly object _sync = new();
    readonly GameOutputFilter _filter = new();
    readonly CheatLaunchNotice _cheats = new();
    StreamWriter? _runLog;
    TrophyTracker? _tracker;
    SessionStats? _stats;
    List<WindowsHdr.Display> _hdr = new();
    string _profileName = "Main";
    bool _outputError;
    public bool Running => _runner.Running;
    public event Action? EventsAvailable;
    void Publish(Event entry)
    {
        _events.Enqueue(entry);
        // Wake the UI even when its render timer is stopped in the background.
        EventsAvailable?.Invoke();
    }
    public LauncherSettings? Settings { get; private set; }
    public NativeGameSession()
    {
        _runner.Output += Observe;
        _runner.Exited += Exited;
    }

    public string? Start(ProfileSession profile)
    {
        if (Running) return UiText.T("The game is already running.");
        string previousIni = Paths.IniPath, previousConfig = Paths.ConfigFile;
        IniFile previousWorkspace = Workspace.Current.Ini;
        try
        {
            var settings = profile.SourceSettings();
            Settings = settings;
            Paths.IniPath = profile.OriginalIni;
            Paths.ConfigFile = profile.ConfigPath;
            var runtimeIni = new IniFile();
            runtimeIni.Load();
            Workspace.Current.Ini = runtimeIni;
            if (settings.Bool("save_backups"))
            {
                try { SaveBackups.Create(settings); }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                { Publish(new("error", Text: backupError.Message)); }
            }
            _profileName = PlayerProfiles.Current(settings);
            string user = GameEnvironment.UserDir(settings);
            Directory.CreateDirectory(user);
            lock (_sync)
            {
                _runLog?.Dispose();
                _runLog = new StreamWriter(new FileStream(Path.Combine(user, "last_run.log"), FileMode.Create,
                    FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                _tracker = new TrophyTracker();
                _tracker.SetUserDir(user);
                _stats = new SessionStats();
                _outputError = false;
                _filter.Reset(false);
                _cheats.Arm(CheatLaunchNotice.AnyEnabled((key, fallback) => runtimeIni.Get(key, fallback)));
                _hdr = OperatingSystem.IsWindows() ? WindowsHdr.Displays() : new();
            }
            MenuAssets.DirectoryPath = profile.Assets;
            string? error = _runner.Start(settings);
            if (error != null) EndFailedStart();
            return error;
        }
        catch (Exception error)
        {
            EndFailedStart();
            return error.GetBaseException().Message;
        }
        finally
        {
            Paths.IniPath = previousIni;
            Paths.ConfigFile = previousConfig;
            Workspace.Current.Ini = previousWorkspace;
        }
    }

    void Observe(string line)
    {
        lock (_sync)
        {
            try
            {
                if (_filter.Accept(line)) _runLog?.WriteLine(line);
                _stats?.Observe(line);
                if (_tracker != null)
                {
                    foreach (int id in _tracker.ObserveSave(line)) { /* Existing save progress stays silent. */ }
                    foreach (int id in _tracker.Observe(line)) Publish(new("trophy", id));
                }
                if (_cheats.Observe(line)) Publish(new("cheater"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (!_outputError) Publish(new("error", Text: error.Message));
                _outputError = true;
            }
        }
    }

    void Exited(int code)
    {
        lock (_sync)
        {
            _cheats.Arm(false);
            _runLog?.Dispose();
            _runLog = null;
            _stats?.Finish();
            if (OperatingSystem.IsWindows() && _hdr.Count > 0) WindowsHdr.Restore(_hdr);
            _hdr.Clear();
            Publish(new("exit", code, Session: _stats, Profile: _profileName));
        }
    }

    void EndFailedStart()
    {
        lock (_sync)
        {
            _cheats.Arm(false);
            _runLog?.Dispose();
            _runLog = null;
            if (OperatingSystem.IsWindows() && _hdr.Count > 0) WindowsHdr.Restore(_hdr);
            _hdr.Clear();
        }
    }
    public bool TryRead(out Event? entry) => _events.TryDequeue(out entry);
    public Task StopAsync() => _runner.StopAsync();
    public void Dispose()
    {
        _runner.Output -= Observe;
        _runner.Exited -= Exited;
        EndFailedStart();
    }
}
