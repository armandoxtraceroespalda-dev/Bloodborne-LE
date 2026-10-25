"""Prevent rebuilding a native settings scene from scrolling input."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
def method(source,name):
 start=source.index('void '+name+'(');start=source.index('{',start);depth=1
 for end in range(start+1,len(source)):
  if source[end]=='{':depth+=1
  if source[end]=='}':
   depth-=1
   if depth==0:return source[start:end+1]
 raise AssertionError(name)
class ScrollContracts(unittest.TestCase):
 def test_scroll_reuses_controls_and_updates_bounded_viewport_resources(self):
  scroll=method((ROOT/'NativeWindow.cs').read_text(),'SetScroll')
  self.assertNotIn('Rebuild(',scroll);self.assertIn('UpdateViewport(',scroll)
 def test_offscreen_rows_stay_in_the_current_scene(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  self.assertNotIn('if (!Canvas.Intersects(b, _viewport))',method(source,'DrawOption'))
  self.assertNotIn('_viewport.Top - _scroll',source)
 def test_window_has_no_standard_caption(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  import re
  style=int(re.search(r'"Bloodborne Launcher",\s*(0x[0-9A-Fa-f]+)',source).group(1),16)
  self.assertEqual(style & 0x00C00000,0) # WS_CAPTION stays absent even with WS_CLIPCHILDREN.
  self.assertNotEqual(style & 0x80000000,0)
  self.assertEqual(0x800F0000 & 0x00C00000,0)
 def test_remaining_tabs_are_native(self):
  source=(ROOT/'NativeWindow.cs').read_text().replace(' ','')
  self.assertNotIn('LaunchOriginal(false)',source)
  self.assertIn('PageCatalog.Build(_page,_profile)',source)
 def test_appearance_uses_existing_game_assets(self):
  source=(ROOT/'NativeWindow.cs').read_text().replace(' ','')
  self.assertIn('Image:"menu-underline"',source)
  self.assertIn('85/255f',source)
 def test_explicit_memory_reclamation_is_isolated_from_input_and_rendering(self):
  for p in ROOT.rglob('*.cs'):
   if p.name=='NavigationMemoryReclaimer.cs':continue
   for api in ['GC.Collect(', 'EmptyWorkingSet(', 'SetProcessWorkingSetSize(']:self.assertNotIn(api,p.read_text())
  source=(ROOT/'NativeWindow.cs').read_text()
  self.assertIn('_navigationMemory.Request()',method(source,'Tick'))
  self.assertIn('_navigationMemory.CancelPending()',method(source,'Switch'))
  self.assertNotIn('_navigationMemory.Request()',method(source,'SetScroll'))
if __name__=='__main__':unittest.main()
