from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'


class MovementLayout(unittest.TestCase):
    def test_slider_value_measurement_uses_the_actual_render_font(self):
        source=(ROOT/'NativeWindow.cs').read_text()
        self.assertIn('_canvas.Measure(number, 18.375f * _scale, mono: true)',source)
        self.assertIn('o.Max.ToString(format, CultureInfo.InvariantCulture) + suffix',source)
        self.assertIn('widest + 12 * _scale',source)
        self.assertIn('Center: true, Mono: true',source)

    def test_icons_do_not_change_titles_or_other_pages(self):
        source=(ROOT/'ReferencePages.cs').read_text()
        self.assertIn('SectionIcons.For(_page, title, existingGlyph)',source)
        self.assertIn('width - 28 * _scale - offset',source)
        controls=(ROOT/'ReferenceControls.cs').read_text()
        self.assertIn('SectionHeading(title, x, width, y)',controls)


if __name__=='__main__':unittest.main()
