namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    float? _sliderPending;
    void FlushSliderDrag()
    {
        if (_sliderPending is not float fraction) return;
        _sliderPending = null;
        if (_drag?.Option?.Kind == Backend.OptionKind.Slider)
            Run(() => _drag.Action?.Invoke(fraction));
    }
}
