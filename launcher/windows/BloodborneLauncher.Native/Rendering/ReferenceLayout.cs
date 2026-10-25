using Vortice.Mathematics;

namespace BloodborneLauncher.Native.Rendering;
/// <summary>Geometry translated from the production SettingRow, SettingsColumns and ControlsPage.</summary>
public static class ReferenceLayout
{
    public const float RowFont = 19.25f, ActionFont = 18.375f, HeadingFont = 21.875f;
    public const float SectionGap = 24, SectionBottom = 16, SplitThreshold = 820;
    public readonly record struct Row(Rect Label, Rect Control, float Height);
    public static Row PlaceRow(float x, float y, float width, float labelWidth, bool slider, bool toggle, bool tight = false, float labelHeight = 23)
    {
        float space = Math.Max(100, width - 20);
        float gap = tight ? 0 : Math.Clamp(space * .02f, 6, 16);
        float minimum = tight ? Math.Max(1, labelWidth + 2) : Math.Clamp(labelWidth, 90, 260);
        bool below = slider || !toggle && space < minimum + gap + 150;
        float height = below ? labelHeight + (slider ? 59 : 61) : toggle ? 52 : 56;
        float columns = below ? space : toggle ? space - 58 : Math.Min(320, Math.Max(minimum, space * .36f));
        var label = new Rect(x + 10, y + 7, columns, below ? labelHeight : toggle ? 34 : 38);
        var control = slider ? new Rect(x + 10 + (space - Math.Min(330, space)) / 2, y + 7 + labelHeight + 6, Math.Min(330, space), 32) : toggle ? new Rect(x + width - 68, y + 7, 58, 34) : below ? new Rect(x + 10, y + 7 + labelHeight + 5, space, 38) : new Rect(label.Right + gap, y + 7, Math.Max(1, space - columns - gap), 38);
        return new(label, control, height);
    }

    public static float FilteredExtent(float bottom, float headingTop, float viewportHeight, bool filtered) => filtered ? Math.Max(bottom, headingTop + viewportHeight) : bottom;
    public readonly record struct BindingColumns(float Action, float Keyboard, float Gamepad, float Reset);
    public static BindingColumns Bindings(float width)
    {
        // ControlsPage.TableRow: 240, 1.6*, 1*, 40. Do not replace with equal thirds.
        float remaining = Math.Max(0, width - 280);
        return new(240, remaining * 1.6f / 2.6f, remaining / 2.6f, 40);
    }
}
