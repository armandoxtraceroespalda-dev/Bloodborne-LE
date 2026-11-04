using System.Security.Cryptography;
using System.Text;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using BloodborneLauncher.Native.Platform;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;
using W = BloodborneLauncher.Native.Platform.Win32;

namespace BloodborneLauncher.Native;

internal sealed partial class NativeWindow
{
    const string RoomInvitePrefix = "LANTERN-ROOM:";
    bool _buildingMenuTextures;
    double _menuTextureProgress;
    string _menuTextureStatus = "";

    void DrawMenuBuildProgress(float x, float width, ref float y)
    {
        if (!_buildingMenuTextures) return;
        float textHeight = WrappedHeight(_menuTextureStatus, 14, width, translate: false);
        Wrapped(_menuTextureStatus, new Rect(x, y, width, textHeight), 14, Gold, translate: false);
        y += textHeight + 8 * _scale;
        Progress(new Rect(x, y, width, 4 * _scale), _menuTextureProgress);
        y += 18 * _scale;
    }

    static string RoomRandom(int length)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        return new string(RandomNumberGenerator.GetBytes(length).Select(b => alphabet[b % alphabet.Length]).ToArray());
    }

    void CreateNativeRoom()
    {
        _profile.EditSource((settings, ini) =>
        {
            ini.Set("seamless_host_room", RoomRandom(20));
            settings.SeamlessHostPassword = RoomRandom(24);
        });
    }

    void DrawSeamlessOptions(float x, float width, ref float y)
    {
        var rows = PageCatalog.Build(5, _profile);
        PageGroup("Seamless co-op", x, width, ref y);
        var seamless = rows.Where(o => o.Section == "Seamless co-op").ToArray();
        bool host = _profile.Ini.Get("seamless_role", "guest") == "host";
        seamless = seamless.Where(o => o.Key switch
        {
            "coop_open_room" => host && !_profile.Ini.Flag("seamless_direct") && _profile.Ini.Get("seamless_server").Length == 0,
            "action:room-copy" or "action:room-new" => host,
            "action:room-paste" => !host,
            "seamless_direct_address" or "seamless_direct_port" => host && _profile.Ini.Flag("seamless_direct"),
            "seamless_direct_host" => !host && _profile.Ini.Flag("seamless_direct"),
            _ => true
        }).ToArray();
        DrawReferenceRows(seamless, x, width, ref y, true);
        PageGroup("Co-op interface", x, width, ref y);
        DrawReferenceRows(rows.Where(o => o.Section == "Co-op interface").ToArray(), x, width, ref y, true);
        PageGroup("Voice chat", x, width, ref y);
        DrawReferenceRows(rows.Where(o => o.Section == "Voice chat").ToArray(), x, width, ref y, true);
    }

    bool UpdatedOnlineAction(string action)
    {
        switch (action)
        {
            case "room-new":
                if (Confirm(UiText.T("Makes a new room with a new invite code; the old code stops working")))
                    CreateNativeRoom();
                return true;
            case "room-copy":
                if (_profile.Ini.Get("seamless_host_room").Length == 0 || _profile.ReadSettings().SeamlessHostPassword.Length == 0)
                    CreateNativeRoom();
                var parts = new List<string> { _profile.Ini.Get("seamless_host_room"), _profile.ReadSettings().SeamlessHostPassword };
                if (_profile.Ini.Flag("seamless_direct"))
                {
                    string address = _profile.Ini.Get("seamless_direct_address").Trim();
                    if (address.Length == 0)
                        address = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a))?.ToString() ?? "";
                    if (address.Length == 0) throw new InvalidOperationException(UiText.T("Enter your address for guests before copying a direct invite."));
                    string port = _profile.Ini.Get("seamless_direct_port", "38765");
                    if (!int.TryParse(port, out int number) || number is < 1024 or > 65535) number = 38765;
                    parts.Add("ws://" + address + ":" + number);
                }
                else if (_profile.Ini.Get("seamless_server").Length > 0 && !_profile.Ini.Get("seamless_server").StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
                    parts.Add(_profile.Ini.Get("seamless_server"));
                string code = RoomInvitePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', parts)))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                NativeDialogs.Copy(_window, code);
                ShowMessage(UiText.T("Invite code copied: send it to your friends"), UiText.T("Invite code"), 0);
                return true;
            case "room-paste":
                ReadNativeInvite(NativeDialogs.Paste(_window));
                return true;
            case "ui4k-build":
                BuildNativeMenuTextures();
                return true;
            case "ui4k-remove":
                if (_buildingMenuTextures) return true;
                if (_game?.Running == true)
                {
                    ShowMessage(UiText.T("Close the game first."), UiText.T("4K menus"), 0);
                    return true;
                }
                if (Confirm(UiText.T("Remove 4K menu textures?")))
                {
                    UiUpscale.Remove();
                    _profile.Set("ui_4k", "0");
                    _graphicsOptions = null;
                    Rebuild();
                }
                return true;
            default:
                return false;
        }
    }

    void ReadNativeInvite(string text)
    {
        const string visitorPrefix = "LANTERN-VISIT:";
        int visitorStart = text.IndexOf(visitorPrefix, StringComparison.OrdinalIgnoreCase);
        bool visitor = visitorStart >= 0;
        string prefix = visitor ? visitorPrefix : RoomInvitePrefix;
        int start = visitor ? visitorStart : text.IndexOf(RoomInvitePrefix, StringComparison.OrdinalIgnoreCase);
        string[] parts = Array.Empty<string>();
        if (start >= 0 && text.Length < 16384)
        {
            string body = new(text[(start + prefix.Length)..]
                .TakeWhile(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
            body = body.Replace('-', '+').Replace('_', '/');
            body = body.PadRight(body.Length + (4 - body.Length % 4) % 4, '=');
            try { parts = Encoding.UTF8.GetString(Convert.FromBase64String(body)).Split('\n'); }
            catch (FormatException) { }
        }
        if (parts.Length is < 2 or > 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts.Any(p => p.Length > 2048))
        {
            ShowMessage(UiText.T("The clipboard holds no invite code. Copy the whole code the host sent you, then paste again."), UiText.T("Invite code"), 0);
            return;
        }
        if (parts[0] == _profile.Ini.Get("seamless_host_room"))
        {
            ShowMessage(UiText.T("That is your own room's code. Friends paste it on their PC to join you."), UiText.T("Invite code"), 0);
            return;
        }
        _profile.EditSource((settings, ini) =>
        {
            ini.Set("seamless_room", parts[0]);
            ini.Set("seamless_visitor", visitor ? "1" : "0");
            settings.SeamlessPassword = parts[1];
            bool direct = parts.Length > 2 && parts[2].StartsWith("ws://", StringComparison.OrdinalIgnoreCase);
            ini.Set("seamless_server", parts.Length > 2 && !direct ? parts[2] : "");
            ini.Set("seamless_direct", direct ? "1" : "0");
            ini.Set("seamless_direct_host", direct ? parts[2][5..] : "");
            ini.Set("seamless_role", "guest");
        });
        _pageRows = null;
        Rebuild();
    }

    void BuildNativeMenuTextures()
    {
        if (_busy) return;
        if (_game?.Running == true)
        {
            ShowMessage(UiText.T("Close the game before building 4K menu textures."), UiText.T("4K menus"), 0);
            return;
        }
        string game = GameEnvironment.GameDir(_profile.ReadSettings());
        _buildingMenuTextures = true;
        _menuTextureProgress = 0;
        _menuTextureStatus = UiText.T("Reading the menu textures from your game files…");
        // The original worker streams to disk; UI updates are marshalled rather
        // than redrawing or retaining texture buffers in the launcher process.
        long lastProgress = 0;
        var progress = new Progress<UiUpscale.Step>(step =>
        {
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref lastProgress) < 250 && step.Fraction < 1) return;
            Interlocked.Exchange(ref lastProgress, now);
            lock (_completions)
                _completions.Enqueue((() =>
                {
                    if (!_buildingMenuTextures) return;
                    _notice = step.Text;
                    _menuTextureStatus = step.Text;
                    _menuTextureProgress = Math.Clamp(step.Fraction, 0, 1);
                }, false));
            W.PostMessage(_window, WorkerMessage, 0, 0);
        });
        Background(async () =>
        {
            try
            {
                await UiUpscale.BuildAsync(game, progress, _cancel.Token);
            }
            catch (Exception error)
            {
                string message = error.GetBaseException().Message;
                return () =>
                {
                    _buildingMenuTextures = false;
                    _menuTextureStatus = "";
                    ShowMessage(message, UiText.T("4K menus"), 0);
                };
            }
            return () =>
            {
                _buildingMenuTextures = false;
                _menuTextureStatus = "";
                _profile.Set("ui_4k", "1");
                _graphicsOptions = null;
                ShowMessage(UiText.T("4K menus are ready"), UiText.T("4K menus"), 0);
            };
        });
        Rebuild();
    }
}
