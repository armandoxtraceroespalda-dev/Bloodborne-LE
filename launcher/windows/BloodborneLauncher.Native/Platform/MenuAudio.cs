using System.Runtime.InteropServices;
using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Platform;
internal sealed class MenuAudio : IDisposable
{
    readonly string _directory;
    readonly MenuSoundGate _gate = new();
    readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    readonly Dictionary<string, (GCHandle Data, int Duration)> _clips = new();
    public bool Muted { get; set; }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    static extern bool PlaySound(nint sound, nint module, uint flags);
    public MenuAudio(string assets, bool muted)
    {
        _directory = Path.Combine(assets, "sounds");
        Muted = muted;
    }

    public void Play(MenuSoundCue cue)
    {
        PlayClip(cue switch
        {
            MenuSoundCue.Hover => "hover",
            MenuSoundCue.Select => "confirm",
            MenuSoundCue.Start => "start",
            _ => "change"
        }, cue);
    }

    public void PlayBack() => PlayClip("back", MenuSoundCue.Change);
    void PlayClip(string name, MenuSoundCue cue)
    {
        if (Muted || _missing.Contains(name))
            return;
        if (!_clips.TryGetValue(name, out var clip))
        {
            var path = Path.Combine(_directory, name + ".wav");
            if (!File.Exists(path)) { _missing.Add(name); return; }
            using var input = File.OpenRead(path);
            if (!MenuWave.TryDuration(input, out int duration))
                return;
            input.Position = 0;
            // Keep long-lived sound buffers off the compacting object heap.
            var bytes = GC.AllocateUninitializedArray<byte>(checked((int)input.Length), pinned: true);
            input.ReadExactly(bytes);
            MenuWave.ApplyGain(bytes, MenuWave.LauncherGain);
            clip = (GCHandle.Alloc(bytes, GCHandleType.Pinned), duration);
            _clips[name] = clip;
        }

        if (_gate.TryPlay(cue, Environment.TickCount64, clip.Duration))
            PlaySound(clip.Data.AddrOfPinnedObject(), 0, 0x0001 | 0x0002 | 0x0004);
    }

    public void Dispose()
    {
        PlaySound(0, 0, 0);
        foreach (var clip in _clips.Values)
            clip.Data.Free();
        _clips.Clear();
        _missing.Clear();
    }
}
