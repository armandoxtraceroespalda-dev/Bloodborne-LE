using W=BloodborneLauncher.Native.Platform.Win32;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    nint _arrowCursor,_textCursor,_handCursor;
    nint SharedCursor(int id)
    {
        ref nint slot=ref (id==32649?ref _handCursor:ref (id==32513?ref _textCursor:ref _arrowCursor));
        if(slot==0)slot=W.LoadCursor(0,id);
        return slot; // System-owned shared cursors must not be destroyed.
    }
}
