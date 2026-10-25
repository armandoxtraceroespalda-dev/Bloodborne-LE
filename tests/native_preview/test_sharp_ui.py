"""Sharp UI must reach the existing engine launch path, not just appear as a toggle."""
from pathlib import Path
import json,unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class SharpUi(unittest.TestCase):
 def test_screen_uses_the_original_boolean_setting(self):
  schema=(ROOT/'Backend/UpstreamBoundary.cs').read_text()
  self.assertIn('new("Screen", "Sharp UI", "exp_sharp_ui", OptionKind.Toggle, Setting: true',schema)
  self.assertEqual(schema.count('"Sharp UI", "exp_sharp_ui"'),1)
  environment=(ROOT.parent/'BloodborneLauncher/Core/GameEnvironment.cs').read_text()
  self.assertIn('if (s.Bool("exp_sharp_ui")) env["BB_SHARP_UI"] = "1";',environment)
  session=(ROOT/'Backend/NativeGameSession.cs').read_text()
  self.assertIn('GameRunner',session)
 def test_title_and_description_are_translated(self):
  key='Draws menus, HUD and text at the output resolution for a sharper interface above 1080p. Applied on the next launch.'
  for language in ['es','fr','de','it','pt-BR','ru','ja']:
   messages=json.loads((ROOT.parent/'BloodborneLauncher/Localization'/(language+'.json')).read_text())
   self.assertTrue(messages['Sharp UI'])
   self.assertTrue(messages[key])
   self.assertNotIn('beta',messages[key].lower())
if __name__=='__main__':unittest.main()
