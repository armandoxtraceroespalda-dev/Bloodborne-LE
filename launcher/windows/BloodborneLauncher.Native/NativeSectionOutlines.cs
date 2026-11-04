using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;

internal sealed partial class NativeWindow
{
    readonly record struct SectionOutline(float X, float Top, float Width, int FirstOp);
    SectionOutline? _sectionOutline;

    void BeginSectionOutline(float x, float top, float width)
    {
        FinishSectionOutline();
        _sectionOutline = new(x, top, width, _ops.Count);
    }

    void FinishSectionOutline()
    {
        if (_sectionOutline is not { } section) return;
        _sectionOutline = null;
        float bottom = section.Top;
        // Read existing layout bounds once; no new surfaces, timers or hit targets are needed.
        for (int i = section.FirstOp; i < _ops.Count; i++)
        {
            var op = _ops[i];
            var box = op.Bounds;
            if (!op.Scrollable || box.Top < section.Top || box.Right <= section.X || box.Left >= section.X + section.Width)
                continue;
            bottom = Math.Max(bottom, box.Bottom);
        }
        if (bottom <= section.Top) return;
        const float inset = 1;
        float left = section.X + inset, right = section.X + section.Width - inset;
        float top = section.Top, end = bottom + 7 * _scale;
        if (right <= left) return;
        // Exactly the same neutral color and one-pixel stroke used by option-row separators.
        var color = new Color4(85 / 255f, 89 / 255f, 87 / 255f, .65f);
        Fill(new Rect(left, top, right - left, 1), color);
        Fill(new Rect(left, end, right - left, 1), color);
        Fill(new Rect(left, top, 1, end - top), color);
        Fill(new Rect(right - 1, top, 1, end - top), color);
    }
}
