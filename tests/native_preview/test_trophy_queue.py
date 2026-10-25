"""Guard sequential, focus-independent presentation and per-pixel feathered styling."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class TrophyQueue(unittest.TestCase):
 def source(self, name): return (ROOT/name).read_text()
 def test_game_events_enqueue_without_replacing_the_active_banner(self):
  game=self.source('NativeGame.cs')
  self.assertIn('QueueTrophy(settings, trophy, entry.Kind == "cheater")',game)
  self.assertNotIn('_trophyBanner?.Dispose()',game)
  queue=self.source('NativeTrophyQueue.cs')
  self.assertIn('_trophyBanner is { IsDisposed: false }) return;',queue)
  self.assertIn('while (_trophyNotices.TryTake(out var notice))',queue)
  self.assertIn('new(64)',queue)
 def test_next_banner_wakes_the_inactive_launcher(self):
  source=self.source('NativeTrophyQueue.cs')
  callback=source.split('void TrophyBannerClosed()',1)[1].split('void AdvanceTrophyQueue()',1)[0]
  self.assertIn('W.PostMessage(_window, TrophyNoticeMessage',callback)
  self.assertNotIn('_active',callback)
  window=self.source('NativeWindow.cs')
  self.assertIn('case TrophyNoticeMessage:\n                    AdvanceTrophyQueue();',window)
  self.assertIn('DisposeTrophyQueue();',window)
 def test_preview_cannot_replace_real_notices(self):
  source=self.source('ReferencePages.cs')+self.source('NativeAchievementNotifications.cs')
  self.assertIn('QueueTrophy(_profile.SourceSettings(), preview: true)',source)
  self.assertNotIn('_trophyBanner?.Dispose()',source)
  queue=self.source('NativeTrophyQueue.cs')
  self.assertIn('if (preview &&',queue)
  self.assertIn('_trophyNotices.Contains(n => n.Preview)',queue)
 def test_banner_uses_menu_font_without_frames_or_underlines(self):
  source=self.source('Platform/TrophyBanner.cs')
  self.assertIn('new Canvas(assets)',source)
  self.assertIn('Ink.SoftSurface',source)
  self.assertIn('Image: "dialog-black"',source)
  self.assertNotIn('menu-frame',source)
  self.assertNotIn('underline',source)
  self.assertNotIn('Ink.Fill',source)
  self.assertIn('new Color4(.89f, .89f, .87f, 1)',source)
 def test_background_edges_use_real_per_pixel_transparency(self):
  banner=self.source('Platform/TrophyBanner.cs')
  self.assertNotIn('SetLayeredWindowAttributes',banner)
  surface=self.source('Platform/LayeredBitmap.cs')
  self.assertIn('UpdateLayeredWindow',surface)
  self.assertIn('Format = 1',surface)
  self.assertIn('Height = -height',surface)
  canvas=self.source('Rendering/Canvas.cs')
  self.assertIn('CreateWicBitmapRenderTarget',canvas)
  self.assertIn('transparent: true',canvas)
  self.assertIn('Vortice.Direct2D1.TextAntialiasMode.Grayscale',canvas)
 def test_animation_does_not_retain_the_canvas_or_rerasterize(self):
  source=self.source('Platform/TrophyBanner.cs')
  self.assertNotIn('readonly Canvas',source)
  self.assertIn('using (var canvas = new Canvas(assets))',source)
  timer=source.split('if (message == 0x113)',1)[1].split('if (message == 0x201)',1)[0]
  self.assertIn('_surface?.Present',timer)
  self.assertNotIn('Rasterize',timer)
  self.assertNotIn('new ',timer)
  cleanup=source.split('public void Dispose()',1)[1]
  self.assertIn('_surface?.Dispose()',cleanup)
  self.assertIn('completed?.Invoke()',cleanup)
if __name__=='__main__': unittest.main()
