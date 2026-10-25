"""Guard the known source-widget geometry; this does not assert pixel parity."""
import unittest,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class ReferenceContracts(unittest.TestCase):
 def test_inactive_nc_frame_is_owned(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  for message in ['0x0083','0x0085','0x0086']:
   self.assertRegex(source,r'case '+message+r'\s*:\s*return [01];')
  self.assertIn('0xfffffffe',source)
 def test_action_highlight_does_not_breathe_like_sidebar(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  start=source.index('void ControlFrame(');end=source.index('void Rebuild(',start)
  self.assertNotIn('Pulse: true',source[start:end]);self.assertIn('Ink.ControlSurface',source[start:end])
 def test_trim_has_the_original_vertical_mirror(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  self.assertIn('FlipY:',source);self.assertIn('b.Bottom',source)
 def test_controls_are_no_longer_equal_thirds(self):
  source=(ROOT/'ReferenceControls.cs').read_text()
  self.assertIn('ReferenceLayout.Bindings(',source);self.assertNotIn('width / 3',source)
  self.assertIn('profile-import',source);self.assertIn('SdlPad.Held()',source)
 def test_keyboard_filter_is_inline(self):
  source=(ROOT/'ReferenceControls.cs').read_text()
  self.assertIn('_searchActive = true',source)
  self.assertNotIn('Edit("Filter actions or keys"',source)
if __name__=='__main__':unittest.main()
