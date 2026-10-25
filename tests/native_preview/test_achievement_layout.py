"""Guard emoji-font isolation, real grayscale decoding and notification grouping."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class AchievementLayout(unittest.TestCase):
 def source(self,path): return (ROOT/path).read_text()
 def test_bottle_is_only_used_for_synthetic_cheater_notification(self):
  source=self.source('Platform/TrophyBanner.cs')
  self.assertIn('if (cheater)\n                    scene.Add',source)
  self.assertIn('"🍼", 34, Center: true, Emoji: true',source)
  self.assertIn('else if (icon != null)',source)
  canvas=self.source('Rendering/Canvas.cs')
  self.assertIn('op.Emoji ? "Segoe UI Emoji"',canvas)
  self.assertIn('DrawTextOptions.EnableColorFont',canvas)
  self.assertIn('op.Weight, op.Emoji',canvas)
  self.assertIn('key.Weight, key.Emoji',canvas)
 def test_locked_icons_decode_original_path_and_convert_once(self):
  source=self.source('Rendering/Canvas.cs')
  self.assertIn('string fileName = gray ? name[..^5] : name;',source)
  self.assertIn('? "menu-glow-half" : fileName) + ".png"',source)
  self.assertIn('ImagePixels.Grayscale(pixels);',source)
  branch=source.split('if (gray)',1)[1].split('if (name == "menu-glow-half-joined")',1)[0]
  self.assertIn('_images[name] = prepared;',branch)
  self.assertIn('AlphaMode.Premultiplied',branch)
  page=self.source('ReferencePages.cs')
  self.assertIn('image + (earned ? "" : "|gray")',page)
  self.assertIn('new Color4(.7f, .7f, .7f, .62f)',page)
 def test_dialogs_use_book_geometry_and_keep_fades(self):
  surface=self.source('NativeDialogSurface.cs')
  self.assertIn('DialogPlacement.Book(_side, _width, _height)',surface)
  for file in ['NativeControlReset.cs','NativeMessages.cs','NativeRecommendations.cs']:
   source=self.source(file)
   self.assertIn('DialogArea',source)
   self.assertNotIn('(_width - width) / 2',source)
   self.assertIn('OpenDialogAnimation()',source)
   self.assertIn('DialogBackdrop(box)',source)
 def test_notification_has_one_heading_and_paired_cards(self):
  page=self.source('ReferencePages.cs')
  self.assertIn('DrawAchievementNotifications(tracker, settings, x, width, ref y)',page)
  source=self.source('NativeAchievementNotifications.cs')
  self.assertEqual(source.count('PageGroup('),1)
  self.assertIn('PageGroup("Notification"',source)
  self.assertIn('DrawReferenceRows(rows, x, width, ref y, true)',source)
  for title in ['Sound file','Try it','Achievement log','Reset achievement progress']:
   self.assertIn('ReferenceCard("'+title+'"',source)
  self.assertIn('float column = two ? (width - 24 * _scale) / 2 : width;',source)
  self.assertIn('y = Math.Max(left, right)',source)
if __name__=='__main__': unittest.main()
