namespace BloodborneLauncher.Native.Rendering;

/// <summary>Two compact rows: a narrow profile selector and six labeled actions.</summary>
internal static class GraphicsProfileLayout
{
    internal readonly record struct Row(float SelectorWidth, float ActionsX, float ButtonWidth, float Gap, bool Stacked = false);
    internal static Row Place(float width)
    {
        float selector = Math.Clamp(width * .26f, 150, 250);
        float gap = Math.Clamp(width * .012f, 6, 12);
        // Very narrow windows put the selector above the two action rows, preserving readable labels.
        if (width < 640)
            return new(Math.Min(360, width), 10, Math.Max(1, (width - 20 - 2 * gap) / 3), gap, true);
        float actionsX = selector + 16;
        float button = Math.Max(1, (width - actionsX - 10 - 2 * gap) / 3);
        return new(selector, actionsX, button, gap);
    }
}
