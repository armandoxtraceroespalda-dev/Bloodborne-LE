"""Promotion preserves user files and retires secondary launcher entry points."""
import importlib.util,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('primary_install',ROOT/'tools/install-primary-launcher.py');install=importlib.util.module_from_spec(spec);spec.loader.exec_module(install)
class PrimaryInstall(unittest.TestCase):
 def test_preserves_gui_and_retires_old_launcher(self):
  with tempfile.TemporaryDirectory() as td:
   root=Path(td);pub=root/'published';app=root/'LauncherApp';preview=root/'NativeLauncherApp'
   for p in [pub,app/'gui',preview/'gui']:p.mkdir(parents=True)
   (pub/install.EXE).write_bytes(b'native');(app/install.EXE).write_bytes(b'legacy')
   (app/'Lantern Engine.exe').write_bytes(b'older');(preview/'Bloodborne Native Preview.exe').write_bytes(b'preview')
   (app/'gui/background.png').write_bytes(b'custom');(preview/'gui/background.png').write_bytes(b'other')
   (preview/'gui/menu-book-leaf.png').write_bytes(b'leaf');(app/'settings.json').write_text('{"keep":true}')
   install.install(pub,app,preview)
   self.assertEqual((app/install.EXE).read_bytes(),b'native')
   self.assertEqual((app/'gui/background.png').read_bytes(),b'custom')
   self.assertEqual((app/'gui/menu-book-leaf.png').read_bytes(),b'leaf')
   self.assertEqual((app/'settings.json').read_text(),'{"keep":true}')
   self.assertEqual((app/'migration-backup'/ (install.EXE+'.previous')).read_bytes(),b'legacy')
   self.assertFalse((app/'Lantern Engine.exe').exists());self.assertFalse((preview/'Bloodborne Native Preview.exe').exists())
   install.install(pub,app,preview)
   self.assertEqual((app/'migration-backup'/(install.EXE+'.previous')).read_bytes(),b'legacy')
 def test_default_commands_and_legacy_path_target_native(self):
  build=(ROOT/'tools/build-launcher-only.sh').read_text()
  self.assertIn('BloodborneLauncher.Native.csproj',build)
  self.assertIn('install-primary-launcher.py',build)
  wrapper=(ROOT/'launcher/windows/BloodborneLauncher/BloodborneLauncher.csproj').read_text()
  self.assertIn('Targets="Publish"',wrapper);self.assertNotIn('UseWPF',wrapper)
 def test_background_events_bypass_the_inactive_render_timer(self):
  source=(ROOT/'launcher/windows/BloodborneLauncher.Native/NativeWindow.cs').read_text()
  self.assertRegex(source,r'case GameEventsMessage:\s*PollGameEvents\(\);\s*return 0;')
  self.assertIn('W.KillTimer(h, 1);\n                        StopLauncherPad();',source)
  game=(ROOT/'launcher/windows/BloodborneLauncher.Native/NativeGame.cs').read_text()
  self.assertIn('_game.EventsAvailable += WakeGameEvents',game)
  self.assertIn('W.PostMessage(_window, GameEventsMessage',game)
  self.assertIn('_game.EventsAvailable -= WakeGameEvents',source)
 def test_launch_does_not_delegate_to_an_old_ui(self):
  core='\n'.join(p.read_text() for p in (ROOT/'launcher/windows/BloodborneLauncher.Native').rglob('*.cs') if 'obj' not in p.parts)
  self.assertNotIn('LaunchOriginal(',core)
  self.assertIn('_runner.Start(settings)',core)
if __name__=='__main__':unittest.main()
