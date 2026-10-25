using BloodborneLauncher.Native.Rendering;

namespace BloodborneLauncher.Native;
internal static class ReferenceLayoutTests
{
    static void Check(bool yes, string message)
    {
        if (!yes)
            throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    public static void Run()
    {
        var row = ReferenceLayout.PlaceRow(0, 0, 700, 100, false, false);
        Check(row.Control.Y == 7 && row.Control.X > row.Label.Right, "source setting row keeps a wide selector beside its label");
        var narrow = ReferenceLayout.PlaceRow(0, 0, 240, 150, false, false);
        Check(narrow.Control.Y > narrow.Label.Bottom, "source row moves the selector below only when label and selector cannot fit");
        var toggle = ReferenceLayout.PlaceRow(0, 0, 400, 120, false, true);
        Check(toggle.Control.Width == 58 && toggle.Control.Height == 34 && toggle.Control.Right == 390, "source switch dimensions and right alignment are retained");
        var slider = ReferenceLayout.PlaceRow(0, 0, 700, 120, true, false);
        Check(slider.Control.Width == 330 && Math.Abs(slider.Control.Center.X - 350) < .01 && slider.Control.Y > slider.Label.Bottom, "source slider value/track group is centered below the title");
        foreach (float scale in new[]
        {
            .7f,
            1f,
            1.3f
        }

        )
        {
            float heading = 900 * scale, top = 100 * scale, height = 600 * scale;
            float scroll = heading - top - 50 * scale;
            float emptyBottom = ReferenceLayout.FilteredExtent(heading + 84 * scale, heading, height, true);
            float filteredScroll = Math.Clamp(scroll, 0, emptyBottom - top - height);
            Check(Math.Abs(filteredScroll - scroll) < .01f, "filtering to zero results preserves the visible search position at scale " + scale);
            float restored = ReferenceLayout.FilteredExtent(heading + 2400 * scale, heading, height, false);
            Check(Math.Abs(Math.Clamp(filteredScroll, 0, restored - top - height) - scroll) < .01f, "clearing the filter keeps the same search position at scale " + scale);
        }

        var columns = ReferenceLayout.Bindings(1000);
        Check(columns.Action == 240 && columns.Reset == 40 && Math.Abs(columns.Keyboard / columns.Gamepad - 1.6) < .001, "source keybinding table uses 240 / 1.6* / 1* / 40 columns");
        Check(ReferenceLayout.RowFont == 19.25f && ReferenceLayout.ActionFont == 18.375f && ReferenceLayout.HeadingFont == 21.875f, "source typography sizes are retained");
        Check(ReferenceLayout.SectionGap == 24 && ReferenceLayout.SplitThreshold == 820, "source section gutter and breakpoint are retained");
    }
}
