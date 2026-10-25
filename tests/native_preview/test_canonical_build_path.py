"""Include-path spelling must remain portable with strict third-party warnings."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('canonical_build_path',ROOT/'tools/canonical-build-path.py')
paths=importlib.util.module_from_spec(spec)
spec.loader.exec_module(paths)

class CanonicalBuildPath(unittest.TestCase):
 def test_preserves_case_and_spaces_without_shell_quoting_in_the_value(self):
  with tempfile.TemporaryDirectory() as temporary:
   directory=Path(temporary)/'Project With Spaces'/'EngineCase'/'GpuCase'
   directory.mkdir(parents=True);header=directory/'win_compat.h';header.write_text('// fixture')
   value=paths.canonical_build_path(header)
   self.assertEqual(value,header.resolve().as_posix())
   self.assertIn('/Project With Spaces/EngineCase/GpuCase/win_compat.h',value)
   self.assertFalse(value.startswith('"'));self.assertNotIn('\\',value)

 def test_missing_header_is_rejected_before_cmake(self):
  with tempfile.TemporaryDirectory() as temporary:
   with self.assertRaises(FileNotFoundError):paths.canonical_build_path(Path(temporary)/'missing.h')

 @unittest.skipUnless(os.name=='nt','Requires a case-insensitive Windows filesystem')
 def test_corrects_lowercase_windows_input_to_real_directory_case(self):
  with tempfile.TemporaryDirectory() as temporary:
   directory=Path(temporary)/'ProjectMixedCase'/'EngineMixedCase'/'GpuMixedCase'
   directory.mkdir(parents=True);header=directory/'win_compat.h';header.write_text('// fixture')
   result=paths.canonical_build_path(str(header).lower())
   self.assertIn('/ProjectMixedCase/EngineMixedCase/GpuMixedCase/win_compat.h',result)

if __name__=='__main__':unittest.main()
