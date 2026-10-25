"""Native Engine preset must use the upstream model and existing settings transport."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class EnginePreset(unittest.TestCase):
 def source(self,path):return (ROOT/path).read_text()
 def test_shared_contract_is_linked_instead_of_reimplemented(self):
  self.assertIn('../BloodborneLauncher/Core/EnginePreset.cs',self.source('BloodborneLauncher.Native.csproj'))
  backend=self.source('Backend/NativeServices.cs')
  self.assertIn('EnginePreset.Current(ReadSettings())',backend)
  self.assertIn('EnginePreset.Apply(settings, value)',backend)
 def test_selector_precedes_existing_engine_switches(self):
  source=self.source('NativePages.cs').split('void ShowEngine()',1)[1].split('void ShowMods()',1)[0]
  self.assertIn('new Option("Engine preset", "Preset", "native-engine-preset"',source)
  self.assertIn('Choices: EnginePreset.Choices',source)
  self.assertLess(source.index('new Option("Engine preset"'),source.index('EngineSwitches.All.Where'))
  self.assertIn('i.Group != EngineSwitches.Graphics',source)
 def test_preset_group_spans_the_available_width(self):
  source=self.source('ReferenceControls.cs')
  self.assertIn('group.Key is "Engine preset" or "Game fixes"',source)
  self.assertIn('group.Key is not ("Engine preset" or "Game fixes") &&',source)
if __name__=='__main__':unittest.main()
