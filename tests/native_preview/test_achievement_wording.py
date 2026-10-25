"""Visible achievement wording and compact normal notifications."""
from pathlib import Path
import unittest,json
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class AchievementWording(unittest.TestCase):
 def test_normal_banner_has_no_generic_unlock_heading(self):
  source=(ROOT/'Platform/TrophyBanner.cs').read_text()
  self.assertNotIn('Trophy earned!',source)
  self.assertNotIn('Error 404: Skill not found.',source)
  self.assertIn('UiText.T(cheater ? "Cheater!" : trophy.Name)',source)
  self.assertIn('UiText.T("I can\'t git gud...")',source)
  self.assertIn('float titleFont = cheater ? 18 : 20;',source)
  self.assertIn('float textTop = (height - textHeight) / 2;',source)
  self.assertIn('float textHeight = titleHeight + (cheater ? 8 + descriptionHeight : 0)',source)
  self.assertIn('if (cheater)\n                    scene.Add(new(Ink.Text, new Rect(94, textTop + titleHeight',source)
 def test_visible_page_wording_uses_achievements(self):
  page=(ROOT/'ReferencePages.cs').read_text()
  self.assertNotIn('weighted by grade',page)
  for phrase in ['All trophies','No trophies earned yet.','Trophy sound']:
   self.assertNotIn('"'+phrase+'"',page)
  self.assertIn('{tracker.Count} of {TrophyTracker.All.Length} achievements',page)
  self.assertIn('{tracker.EarnedPoints} of {tracker.TotalPoints} points',page)
  notification=(ROOT/'NativeAchievementNotifications.cs').read_text()
  self.assertIn('"Achievement log"',notification)
  self.assertIn('"Reset achievement progress"',notification)
  self.assertIn('"trophy-progress-reset"',notification)
 def test_new_translations_preserve_format_placeholders(self):
  for language in ['es','fr','de','it','pt-BR','ru','ja']:
   messages=json.loads((ROOT.parent/'BloodborneLauncher/Localization'/(language+'.json')).read_text())
   for key in ['{0} of {1} points','{0} of {1} achievements']:
    self.assertEqual(messages[key].count('{0}'),1)
    self.assertEqual(messages[key].count('{1}'),1)
   for key in ['Cheater!',"I can't git gud...",'Achievement sound','All achievements']:
    self.assertTrue(messages[key])
if __name__=='__main__':unittest.main()
