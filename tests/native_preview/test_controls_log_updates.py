"""Guard device-isolated resets and visible-only background log refresh."""
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'


class ControlsAndLog(unittest.TestCase):
    def test_reset_buttons_open_themed_confirmation(self):
        source=(ROOT/'ReferenceControls.cs').read_text()
        self.assertIn('AskControlReset(true)',source)
        self.assertIn('AskControlReset(false)',source)
        self.assertIn('Button("Reset",',source)
        prompt=(ROOT/'NativeControlReset.cs').read_text()
        self.assertIn('DialogBackdrop(box)',prompt)
        self.assertNotIn('menu-frame',prompt)
        self.assertNotIn('MessageBox',prompt)
        self.assertIn('new(_profile.InputDevice, all)',prompt)

    def test_background_log_has_its_own_slow_timer(self):
        source=(ROOT/'NativeLogRefresh.cs').read_text()
        self.assertIn('!_active && _game?.Running == true',source)
        self.assertIn('_page == 8 && _moreTab == 1',source)
        self.assertIn('!W.IsIconic(_window)',source)
        self.assertIn('if (needed == _backgroundLogTimer) return;',source)
        self.assertIn('BackgroundLogTimer, 500',source)
        self.assertIn('PollReferenceOutput(now)',source)
        self.assertIn('TryReclaimMemory(now)',source)
        callback=source[source.index('void RefreshBackgroundLog()'):source.index('void TryReclaimMemory(')]
        self.assertNotIn('Invalidate()',callback)
        window=(ROOT/'NativeWindow.cs').read_text()
        self.assertIn('(nuint)wp == BackgroundLogTimer',window)
        self.assertIn('W.KillTimer(h, 1);\n                        StopLauncherPad();',window)

    def test_requested_headings_are_removed_without_losing_live_options(self):
        source=(ROOT/'ReferencePages.cs').read_text()
        for title in ['Play','Server','Bloodborne PC']:
            self.assertNotIn('PageGroup("'+title+'",',source)
        self.assertIn('"Play online", "online_enabled"',source)
        self.assertIn('"Server", "online_pool"',source)
        self.assertNotIn('Symbol(SectionIcons.For(_page, "All achievements")',source)

    def test_gpu_selector_keeps_its_row_and_title_metadata(self):
        source=(ROOT/'NativeWindow.cs').read_text()
        self.assertIn('Heading = true, Icon = "\\uE950"',source)
        self.assertIn('DrawOption(gpu, new Rect(x, y, width, 68 * _scale))',source)


if __name__=='__main__':unittest.main()
