"""Keep physical gamepad preference separate from keyboard/gamepad mode."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class PreferredGamepad(unittest.TestCase):
 def source(self,path):return (ROOT/path).read_text()
 def test_only_gamepad_mode_draws_preference_beside_profile(self):
  source=self.source('ReferenceControls.cs')
  self.assertIn('bool showPreferred = _profile.InputDevice == "controller";',source)
  self.assertIn('if (showPreferred)',source)
  self.assertIn('DrawPreferredGamepad(preferredX, y,',source)
  self.assertIn('preferredColumn - 50 * _scale',source)
  engine=self.source('NativePages.cs')
  self.assertIn('i.Env != "BB_GAMEPAD"',engine)
 def test_preference_is_a_real_choice_with_friendly_untranslated_names(self):
  source=self.source('Backend/PreferredGamepads.cs')
  self.assertIn('OptionKind.Choice',source)
  self.assertIn('new("Automatic", "")',source)
  self.assertIn('Choice.Literal(device.Name + " · " + guid, guid)',source)
  self.assertIn('Saved preference: {0}',source)
  self.assertIn('!device.Supported',source)
  view=self.source('NativePreferredGamepad.cs')
  self.assertIn('ControlFrame(selector, index)',view)
  self.assertNotIn('QueueNativeText',view)
 def test_discovery_is_on_demand_and_releases_native_resources(self):
  source=self.source('NativePreferredGamepad.cs')
  self.assertIn('if (_game?.Running == true) return;',source)
  self.assertIn('finally { if (_padCapture == null) SdlPad.Stop(); }',source)
  self.assertNotIn('SetTimer',source)
  window=self.source('NativeWindow.cs')
  self.assertIn('if (o.Key == PreferredGamepads.Key)\n        {\n            RefreshPreferredPads();',window)
  self.assertIn('_preferredPads = null;',window)
 def test_optional_sdl_guid_exports_follow_library_lifetime(self):
  source=(ROOT.parent/'BloodborneLauncher/Core/SdlPad.cs').read_text()
  self.assertIn('NativeLibrary.TryGetExport(lib, "SDL_GetGamepadGUIDForID"',source)
  self.assertIn('NativeLibrary.TryGetExport(lib, "SDL_GUIDToString"',source)
  self.assertIn('var buffer = new byte[33];',source)
  self.assertIn('_guidForId = null; _guidString = null;',source)
if __name__=='__main__':unittest.main()
