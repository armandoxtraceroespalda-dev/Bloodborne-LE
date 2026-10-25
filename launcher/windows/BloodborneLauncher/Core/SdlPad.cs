using System.IO;
using System.Runtime.InteropServices;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// The engine's own SDL3 (out\SDL3.dll in the package), used to see controllers exactly as the
    /// game does: the same names, the same button mapping (gamecontrollerdb.txt included) and the
    /// same button names for pad.* lines. Only used while the game is not running, so the two
    /// never hold a pad at the same time.
    /// </summary>
    public static class SdlPad
    {
        public sealed record Device(string Name, bool Supported)
        {
            public string Guid { get; init; } = "";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SdlGuid { public ulong First, Second; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate SdlGuid GuidForIdFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GuidStringFn(SdlGuid guid, [Out] byte[] buffer, int capacity);
        private static GuidForIdFn? _guidForId;
        private static GuidStringFn? _guidString;

        private const uint InitGamepad = 0x2000;
        private const int ButtonCount = 26, AxisLeftTrigger = 4, AxisRightTrigger = 5;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool SetHintFn([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool InitFn(uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void QuitFn(uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ListFn(out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeFn(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ById(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool IsById(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)]
        private delegate bool ButtonFn(IntPtr pad, int button);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate short AxisFn(IntPtr pad, int axis);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ButtonNameFn(int button);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseFn(IntPtr pad);

        private static bool _loaded, _failed, _initialised;
        private static string _failedPath="";
        private static IntPtr _library;
        private static VoidFn? _shutdown;
        private static SetHintFn? _setHint;
        private static InitFn? _init;
        private static QuitFn? _quit;
        private static ListFn? _gamepads, _joysticks;
        private static FreeFn? _free;
        private static ById? _open, _nameForId, _joystickNameForId;
        private static IsById? _isGamepad;
        private static VoidFn? _update;
        private static ButtonFn? _button;
        private static AxisFn? _axis;
        private static ButtonNameFn? _buttonName;
        private static CloseFn? _close;
        private static readonly Dictionary<uint, IntPtr> Open = new();
        private static readonly HashSet<uint> Seen = new();
        private static readonly List<uint> Removed = new();
        private static readonly string?[] ButtonNames = new string?[ButtonCount];

        /// <summary>False when the package has no SDL3.dll (the launcher falls back to its own detection).</summary>
        public static bool Available => Load();

        private static bool Load()
        {
            if (_loaded) return true;
            var dll = Path.Combine(Paths.BinariesDir, "SDL3.dll");
            if(_failed && _failedPath==dll)return false;
            _failed=false;_failedPath=dll;
            try
            {
                if (!File.Exists(dll)) { _failed = true; return false; }
                var lib = _library = NativeLibrary.Load(dll);
                T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
                _setHint = Get<SetHintFn>("SDL_SetHint");
                _init = Get<InitFn>("SDL_InitSubSystem");
                _quit = Get<QuitFn>("SDL_QuitSubSystem");
                _shutdown = Get<VoidFn>("SDL_Quit");
                _gamepads = Get<ListFn>("SDL_GetGamepads");
                _joysticks = Get<ListFn>("SDL_GetJoysticks");
                _free = Get<FreeFn>("SDL_free");
                _open = Get<ById>("SDL_OpenGamepad");
                _nameForId = Get<ById>("SDL_GetGamepadNameForID");
                _joystickNameForId = Get<ById>("SDL_GetJoystickNameForID");
                _isGamepad = Get<IsById>("SDL_IsGamepad");
                // Optional identity exports must not break existing detection/capture on older DLLs.
                if (NativeLibrary.TryGetExport(lib, "SDL_GetGamepadGUIDForID", out var guidExport) &&
                    NativeLibrary.TryGetExport(lib, "SDL_GUIDToString", out var stringExport))
                {
                    _guidForId = Marshal.GetDelegateForFunctionPointer<GuidForIdFn>(guidExport);
                    _guidString = Marshal.GetDelegateForFunctionPointer<GuidStringFn>(stringExport);
                }
                _update = Get<VoidFn>("SDL_UpdateGamepads");
                _button = Get<ButtonFn>("SDL_GetGamepadButton");
                _axis = Get<AxisFn>("SDL_GetGamepadAxis");
                _buttonName = Get<ButtonNameFn>("SDL_GetGamepadStringForButton");
                _close = Get<CloseFn>("SDL_CloseGamepad");
                for(int button=0;button<ButtonCount;button++)
                    ButtonNames[button]=Marshal.PtrToStringUTF8(_buttonName!(button));
                _loaded = true;
            }
            catch
            {
                ReleaseLibrary();
                _failed = true;
            }
            return _loaded;
        }

        private static bool Start()
        {
            if (_initialised) return true;
            if (!Load()) return false;
            // The same hints the launcher gives the game (GameEnvironment).
            foreach (var (key, value) in GameEnvironment.SdlHints())
                _setHint!(key, value);
            _setHint!("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            _initialised = _init!(InitGamepad);
            return _initialised;
        }

        /// <summary>Lets go of every pad (before the game starts, or when the page closes).</summary>
        public static void Stop()
        {
            if (_library == IntPtr.Zero) return;
            foreach (var pad in Open.Values) _close!(pad);
            Open.Clear();
            if (_initialised) _quit!(InitGamepad);
            // SDL belongs only to this launcher's controller preview. The game is another process.
            _shutdown?.Invoke();
            _initialised = false;
            ReleaseLibrary();
        }

        private static void ReleaseLibrary()
        {
            // No callback or native pointer may outlive the module reference.
            var library = _library; _library = IntPtr.Zero;
            _setHint = null; _init = null; _quit = null; _shutdown = null;
            _gamepads = null; _joysticks = null; _free = null;
            _open = null; _nameForId = null; _joystickNameForId = null; _isGamepad = null;
            _update = null; _button = null; _axis = null; _buttonName = null; _close = null;
            _guidForId = null; _guidString = null;
            _nextDeviceScan = 0;
            _loaded = false;
            Seen.Clear();Removed.Clear();Array.Clear(ButtonNames);
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }

        private static List<uint> Ids(ListFn list)
        {
            var ids = new List<uint>();
            var array = list(out var count);
            if (array == IntPtr.Zero) return ids;
            for (var i = 0; i < count; i++) ids.Add((uint)Marshal.ReadInt32(array, i * 4));
            _free!(array);
            return ids;
        }

        /// <summary>The controllers connected now; Supported is false for one SDL sees but cannot map.</summary>
        public static List<Device>? Devices()
        {
            if (!Start()) return null;
            _update!();
            var devices = new List<Device>();
            foreach (var id in Ids(_joysticks!))
            {
                var supported = _isGamepad!(id);
                var name = Marshal.PtrToStringUTF8(supported ? _nameForId!(id) : _joystickNameForId!(id));
                string guid = "";
                if (supported && _guidForId != null && _guidString != null)
                {
                    var buffer = new byte[33];
                    _guidString(_guidForId(id), buffer, buffer.Length);
                    int end = Array.IndexOf(buffer, (byte)0);
                    guid = System.Text.Encoding.ASCII.GetString(buffer, 0, end >= 0 ? end : 32);
                }
                devices.Add(new Device(string.IsNullOrWhiteSpace(name) ? UiText.T("Game controller") : name!, supported) { Guid = guid });
            }
            return devices;
        }

        /// <summary>The pads' state in coarse form (held buttons, stick positions in eighths). A receiver
        /// stays connected while its pad is switched off, so only a change in this shows the pad is on.</summary>
        public static ulong State()
        {
            if(!Start())return 0;
            _update!();RefreshOpen();
            ulong state=14695981039346656037UL;
            foreach(var pair in Open)
            {
                // Hash coarse physical values directly: no Held set, strings or LINQ per poll.
                Mix(ref state,pair.Key);
                ulong buttons=0;
                for(int button=0;button<ButtonCount;button++)if(_button!(pair.Value,button))buttons|=1UL<<button;
                if(_axis!(pair.Value,AxisLeftTrigger)>16000)buttons|=1UL<<ButtonCount;
                if(_axis!(pair.Value,AxisRightTrigger)>16000)buttons|=1UL<<(ButtonCount+1);
                Mix(ref state,buttons);
                for(int axis=0;axis<4;axis++)Mix(ref state,unchecked((ulong)(_axis!(pair.Value,axis)/8192)));
            }
            return state;
        }
        private static void Mix(ref ulong hash,ulong value) => hash=unchecked((hash^value)*1099511628211UL);

        private static long _nextDeviceScan;
        public static void RefreshDevices() => _nextDeviceScan = 0;
        private static void RefreshOpen()
        {
            long now = Environment.TickCount64;
            if (now < _nextDeviceScan) return;
            _nextDeviceScan = now + 750;
            Seen.Clear();Removed.Clear();
            var ids=_gamepads!(out int count);
            try
            {
                if(ids!=IntPtr.Zero)for(int index=0;index<count;index++)
                {
                    uint id=(uint)Marshal.ReadInt32(ids,index*4);Seen.Add(id);
                    if(Open.ContainsKey(id))continue;
                    var pad=_open!(id);if(pad!=IntPtr.Zero)Open[id]=pad;
                }
                foreach(var pair in Open)if(!Seen.Contains(pair.Key))Removed.Add(pair.Key);
                foreach(uint id in Removed) { _close!(Open[id]);Open.Remove(id); }
            }
            finally { if(ids!=IntPtr.Zero)_free!(ids); }
        }

        public readonly record struct MenuInput(uint Buttons, short LeftX, short LeftY, short RightY, int Connected);
        /// <summary>Allocation-free coarse input for launcher navigation; never used by the game.</summary>
        public static MenuInput ReadMenuInput()
        {
            if (!Start()) return default;
            _update!(); RefreshOpen();
            uint buttons=0; short x=0,y=0,scroll=0;
            foreach (var pad in Open.Values)
            {
                for(int button=0;button<15;button++) if(_button!(pad,button)) buttons |= 1u << button;
                short px=_axis!(pad,0), py=_axis!(pad,1), sy=_axis!(pad,3);
                if(Math.Abs((int)px)>Math.Abs((int)x))x=px;
                if(Math.Abs((int)py)>Math.Abs((int)y))y=py;
                if(Math.Abs((int)sy)>Math.Abs((int)scroll))scroll=sy;
            }
            return new(buttons,x,y,scroll,Open.Count);
        }

        /// <summary>The buttons held now on any pad, as SDL names (a, leftshoulder, …, lefttrigger, righttrigger).</summary>
        public static HashSet<string> Held()
        {
            var held = new HashSet<string>();
            if (!Start()) return held;
            _update!();
            RefreshOpen();
            foreach (var pad in Open.Values)
            {
                for (var b = 0; b < ButtonCount; b++)
                    if (_button!(pad, b) && ButtonNames[b] is { Length: > 0 } name)
                        held.Add(name);
                if (_axis!(pad, AxisLeftTrigger) > 16000) held.Add("lefttrigger");
                if (_axis!(pad, AxisRightTrigger) > 16000) held.Add("righttrigger");
            }
            return held;
        }
    }
}
