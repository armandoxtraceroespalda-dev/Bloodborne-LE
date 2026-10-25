"""Root executable deployment preserves artwork and isolates support files."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('root_install',ROOT/'tools/install-primary-launcher.py')
installer=importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

class RootLauncherInstall(unittest.TestCase):
 def test_root_layout_migrates_gui_and_keeps_support_and_settings(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=Path(temporary)/'package';support=root/'LauncherApp';published=Path(temporary)/'publish';preview=root/'NativeLauncherApp'
   for directory in [root/'gui',support/'gui',published/'fonts',preview]:directory.mkdir(parents=True,exist_ok=True)
   (published/installer.EXE).write_bytes(b'new native launcher')
   (published/'fonts/fallback.otf').write_bytes(b'font')
   (support/installer.EXE).write_bytes(b'old entry point')
   (support/'gui/background.png').write_bytes(b'old artwork')
   (support/'gui/menu-book-leaf.png').write_bytes(b'leaf')
   (root/'gui/background.png').write_bytes(b'custom artwork')
   (root/'settings.json').write_text('{"preserve":true}')
   (root/'.gitignore').write_text('out/\nLauncherApp/\n')
   installer.install(published,root,preview,support)
   self.assertEqual((root/installer.EXE).read_bytes(),b'new native launcher')
   self.assertFalse((support/installer.EXE).exists())
   self.assertEqual((support/'fonts/fallback.otf').read_bytes(),b'font')
   self.assertFalse((root/'fonts').exists())
   self.assertEqual((root/'gui/background.png').read_bytes(),b'custom artwork')
   self.assertEqual((root/'gui/menu-book-leaf.png').read_bytes(),b'leaf')
   self.assertEqual((root/'settings.json').read_text(),'{"preserve":true}')
   ignore=(root/'.gitignore').read_text()
   self.assertIn('out/',ignore);self.assertIn('/gui/',ignore);self.assertIn('/'+installer.EXE,ignore)
   installer.install(published,root,preview,support)
   self.assertEqual((root/'.gitignore').read_text(),ignore)

if __name__=='__main__':unittest.main()
