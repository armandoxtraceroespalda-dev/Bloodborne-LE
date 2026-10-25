"""One launcher support folder must preserve source files and migrate old data."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('consolidated_install',ROOT/'tools/install-primary-launcher.py')
installer=importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

class LauncherFolderConsolidation(unittest.TestCase):
 def test_old_folders_are_archived_and_not_recreated(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=Path(temporary)/'project';published=Path(temporary)/'published'
   support=root/'launcher';legacy=root/'LauncherApp';preview=root/'NativeLauncherApp'
   for directory in [support/'windows',legacy/'gui',preview/'gui',published/'fonts',root/'gui']:
    directory.mkdir(parents=True,exist_ok=True)
   (support/'windows/keep.cs').write_text('// keep source')
   (published/installer.EXE).write_bytes(b'new entry point')
   (published/'fonts/fallback.otf').write_bytes(b'new font')
   (legacy/installer.EXE).write_bytes(b'old entry point')
   (legacy/'gui/background.png').write_bytes(b'old artwork')
   (preview/'gui/menu-book-leaf.png').write_bytes(b'leaf')
   (legacy/'settings.json').write_text('{"keep":true}')
   (root/'gui/background.png').write_bytes(b'custom artwork')
   installer.install(published,root,preview,support)
   self.assertFalse(legacy.exists());self.assertFalse(preview.exists())
   self.assertEqual((root/installer.EXE).read_bytes(),b'new entry point')
   self.assertEqual((support/'fonts/fallback.otf').read_bytes(),b'new font')
   self.assertEqual((support/'windows/keep.cs').read_text(),'// keep source')
   self.assertEqual((root/'gui/background.png').read_bytes(),b'custom artwork')
   self.assertEqual((root/'gui/menu-book-leaf.png').read_bytes(),b'leaf')
   archived=support/'migration-backup/Legacy-LauncherApp'
   self.assertEqual((archived/'settings.json').read_text(),'{"keep":true}')
   self.assertEqual((archived/'gui/background.png').read_bytes(),b'old artwork')
   rules=(root/'.gitignore').read_text().splitlines()
   self.assertIn('/launcher/migration-backup/',rules)
   self.assertNotIn('/launcher/',rules)
   installer.install(published,root,preview,support)
   self.assertFalse(legacy.exists());self.assertFalse(preview.exists())
   self.assertEqual((archived/'settings.json').read_text(),'{"keep":true}')

if __name__=='__main__':unittest.main()
