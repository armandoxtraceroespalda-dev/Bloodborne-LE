"""Preserve existing card icons and reserve space for new heading glyphs."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'


class SectionIconContracts(unittest.TestCase):
    def test_existing_card_icons_remain_explicit(self):
        source=(ROOT/'ReferencePages.cs').read_text()
        start=source.index('void ReferenceCard(');end=source.index('void FolderCard(',start)
        card=source[start:end]
        self.assertNotIn('SectionIcons.',card)
        self.assertIn('Symbol(glyph,',card)
        self.assertIn('FolderCard("Game folder"',source)

    def test_special_heading_does_not_push_reset_all(self):
        source=(ROOT/'ReferenceControls.cs').read_text()
        self.assertIn('heading - 32 * _scale',source)
        self.assertIn('x + width - resetWidth',source)
        self.assertIn('x + heading + 14 * _scale',source)

    def test_card_heading_and_graphics_catalog_stay_separate(self):
        source=(ROOT/'Rendering/SectionIcons.cs').read_text()
        self.assertLess(source.index('if (existing.Length > 0)'),source.index('if (page == 1)'))
        self.assertIn('return GraphicsSectionIcons.For(title)',source)
        self.assertNotIn('new ',source)


if __name__=='__main__':unittest.main()
