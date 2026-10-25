"""Guard retained-scene fading and completion/input ordering."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]/'launcher/windows/BloodborneLauncher.Native'
class DialogAnimation(unittest.TestCase):
 def test_fade_changes_render_opacity_without_rebuilding(self):
  source=(ROOT/'NativeDialogAnimation.cs').read_text()
  tick=source.split('void TickDialogAnimation()',1)[1]
  self.assertNotIn('RefreshDialogSurface',tick)
  self.assertNotIn('Rebuild()',tick)
  self.assertNotIn('SyncSceneResources',tick)
  self.assertIn('W.KillTimer(_window, DialogAnimationTimer)',tick)
  self.assertIn('if (complete != null) Run(complete)',tick)
  canvas=(ROOT/'Rendering/Canvas.cs').read_text()
  self.assertIn('if (op.Dialog) alpha *= dialogFade;',canvas)
  self.assertIn('bool Dialog = false',canvas)
 def test_dialog_open_and_close_paths_share_the_animation(self):
  for file,opener,closer in [('NativeControlReset.cs','AskControlReset','CloseControlReset'),('NativeRecommendations.cs','OpenRecommendations','CloseRecommendations'),('NativeMessages.cs','ShowMessage','FinishMessage')]:
   source=(ROOT/file).read_text()
   self.assertIn(opener,source)
   self.assertIn(closer,source)
   self.assertIn('OpenDialogAnimation()',source)
   self.assertIn('CloseDialogAnimation(',source)
  recommendations=(ROOT/'NativeRecommendations.cs').read_text()
  self.assertIn('ApplyRecommendations() => CloseDialogAnimation(FinishRecommendations)',recommendations)
 def test_input_and_memory_are_gated_while_closing(self):
  window=(ROOT/'NativeWindow.cs').read_text()
  self.assertIn('if (_dialogClosing) return;',window)
  self.assertIn('if (_dialogClosing) return 0;',window)
  self.assertIn('Dialog = _drawingDialog',window)
  self.assertIn('DialogAnimationTimer) { TickDialogAnimation(); return 0; }',window)
  self.assertIn('!_dialogFade.Animating',(ROOT/'NativeLogRefresh.cs').read_text())
  self.assertIn('if (_dialogClosing) return;',(ROOT/'NativeText.cs').read_text())
if __name__=='__main__': unittest.main()
