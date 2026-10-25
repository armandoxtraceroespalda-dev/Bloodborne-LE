"""Startup must not prepare the other pages or start asset extraction workers."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
def method(source,name):
 signature=('public NativeWindow' if name=='NativeWindow' else 'public int Run' if name=='Run' else 'void '+name)+'('
 start=source.index(signature);start=source.index('{',start);depth=1
 for end in range(start+1,len(source)):
  if source[end]=='{':depth+=1
  if source[end]=='}':
   depth-=1
   if depth==0:return source[start:end+1]
 raise AssertionError(name)
class StartupContracts(unittest.TestCase):
 def test_window_starts_without_settings_page_data(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  ctor=method(source,'NativeWindow')
  for call in ['GraphicsSchema.Build(', 'PageCatalog.Build(', 'GpuAdapters.All(', 'WindowsMonitors.All(', 'PrepareBackSoundAsync(']:self.assertNotIn(call,ctor)
  self.assertIn('_graphicsOptions ??= GraphicsSchema.Build()',source)
 def test_start_has_no_dependency_on_full_choice_catalogs(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  self.assertNotIn('Choices.',method(source,'Home'))
  summary=(ROOT/'Backend/StartupSummary.cs').read_text()
  self.assertNotIn('Choices.',summary.replace('Upstream Choices still','Upstream selectors still'))
  self.assertNotIn('Hardware.Detect(',summary)
 def test_back_asset_preparation_is_requested_on_use(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  self.assertNotIn('PrepareBackSoundAsync(',method(source,'Run'))
  text=(ROOT/'NativeText.cs').read_text()
  self.assertIn('_backPreparation ??= PrepareBackSoundAsync()',method(text,'ReturnToStart'))
 def test_leaving_graphics_discards_its_page_data(self):
  source=(ROOT/'NativeWindow.cs').read_text()
  self.assertIn('_graphicsOptions = null',method(source,'ReleasePageData'))
if __name__=='__main__':unittest.main()
