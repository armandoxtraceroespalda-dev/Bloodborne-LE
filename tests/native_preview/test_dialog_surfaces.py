"""Compact dialogs reuse the retained page and release temporary modal resources."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'


class DialogSurfaces(unittest.TestCase):
    def test_dialogs_use_only_the_shared_feathered_backdrop(self):
        surface=(ROOT/'NativeDialogSurface.cs').read_text()
        self.assertIn('new Color4(0, 0, 0, .75f)',surface)
        self.assertIn('Image: "dialog-black"',surface)
        for name in ['NativeControlReset.cs','NativeRecommendations.cs']:
            source=(ROOT/name).read_text()
            self.assertIn('DialogBackdrop(box)',source)
            self.assertNotIn('menu-frame',source)
            self.assertNotIn('Fill(new Rect(0, 34, _width',source)

    def test_dialog_refresh_keeps_the_retained_page(self):
        source=(ROOT/'NativeDialogSurface.cs').read_text()
        for name in ['Ops','Hits','Glows']:
            self.assertIn('_dialogBase'+name,source)
        self.assertIn('_ops.RemoveRange(_dialogBaseOps',source)
        self.assertIn('_hits.RemoveRange(_dialogBaseHits',source)
        self.assertIn('SyncSceneResources()',source)
        self.assertIn('if (lifecycle)',source)
        self.assertIn('_canvas.ReleaseBuildMetrics()',source)
        self.assertIn('_navigationMemory.Request()',source)
        window=(ROOT/'NativeWindow.cs').read_text()
        self.assertIn('CaptureDialogBase()',window)

    def test_information_tooltip_is_the_only_reset_modal_hover_tip(self):
        reset=(ROOT/'NativeControlReset.cs').read_text()
        self.assertIn('"controls-reset-info", _ => { }, text.Tooltip',reset)
        self.assertIn('TextAt("!", info',reset)
        window=(ROOT/'NativeWindow.cs').read_text()
        self.assertIn('ModalTooltipAllowed &&',window)
        surface=(ROOT/'NativeDialogSurface.cs').read_text()
        self.assertIn('_hits[_hover].Id == "controls-reset-info"',surface)

    def test_profile_uses_the_existing_player_silhouette(self):
        controls=(ROOT/'ReferenceControls.cs').read_text()
        self.assertIn(r'Symbol("\uE77B", new Rect(x + 10 * _scale, y + 7 * _scale',controls)
        pages=(ROOT/'ReferencePages.cs').read_text()
        self.assertIn(r'ReferenceCard("Player profile",',pages)
        player=pages[pages.index('ReferenceCard("Player profile",'):]
        self.assertIn(r'"\uE77B"',player[:400])

    def test_recommendation_scrolling_does_not_repeat_lifetime_cleanup(self):
        source=(ROOT/'NativeRecommendations.cs').read_text()
        self.assertIn('RefreshDialogSurface(lifecycle: false)',source)
        self.assertIn('WrappedHeight(row.Detail',source)
        self.assertIn('_recommendationScrollMax = Math.Max(0, contentHeight - viewport.Height)',source)


if __name__=='__main__':unittest.main()
