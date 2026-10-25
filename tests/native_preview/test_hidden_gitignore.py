"""Updating local output rules must not recreate a hidden Windows .gitignore."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('hidden_ignore_install',ROOT/'tools/install-primary-launcher.py')
installer=importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

class HiddenIgnore(unittest.TestCase):
 def test_keeps_existing_bytes_and_never_opens_with_truncation(self):
  with tempfile.TemporaryDirectory() as temporary:
   path=Path(temporary)/'.gitignore'
   original=b'\xef\xbb\xbf# Local outputs\r\nout/\r\n/gui/'
   path.write_bytes(original)
   open_path=Path.open
   def guarded_open(target,mode='r',*args,**kwargs):
    if target==path and ('w' in mode or 'x' in mode):
     raise PermissionError('Windows hidden files reject CREATE_ALWAYS')
    return open_path(target,mode,*args,**kwargs)
   with patch.object(Path,'open',guarded_open):
    installer.add_ignore_rules(path,['/gui/','/engine/lantern.ini'])
   expected=original+b'\r\n/engine/lantern.ini\r\n'
   self.assertEqual(path.read_bytes(),expected)
   before=path.stat().st_mtime_ns
   installer.add_ignore_rules(path,['/gui/','/engine/lantern.ini'])
   self.assertEqual(path.read_bytes(),expected)
   self.assertEqual(path.stat().st_mtime_ns,before)

 def test_creates_missing_ignore_file_without_discarding_rules(self):
  with tempfile.TemporaryDirectory() as temporary:
   path=Path(temporary)/'.gitignore'
   installer.add_ignore_rules(path,['/gui/','/engine/lantern.ini'])
   self.assertEqual(path.read_bytes(),b'/gui/\n/engine/lantern.ini\n')

 @unittest.skipUnless(os.name=='nt','Requires Windows file attributes')
 def test_updates_an_actual_hidden_windows_file(self):
  import ctypes
  with tempfile.TemporaryDirectory() as temporary:
   path=Path(temporary)/'.gitignore';path.write_bytes(b'out/\r\n')
   api=ctypes.windll.kernel32
   attributes=api.GetFileAttributesW(str(path))
   self.assertTrue(api.SetFileAttributesW(str(path),attributes|2))
   try:
    installer.add_ignore_rules(path,['/engine/lantern.ini'])
    self.assertEqual(path.read_bytes(),b'out/\r\n/engine/lantern.ini\r\n')
    self.assertTrue(api.GetFileAttributesW(str(path))&2)
   finally:
    api.SetFileAttributesW(str(path),attributes)

if __name__=='__main__':unittest.main()
