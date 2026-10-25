#!/usr/bin/env python3
"""Contract drift tests use the checkout's own upstream files; no network access."""
import importlib.util,json,shutil,tempfile,unittest,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('native_audit',ROOT/'tools/audit-native-upstream.py');audit=importlib.util.module_from_spec(spec);spec.loader.exec_module(audit)
class Contracts(unittest.TestCase):
 def test_contract_links_exist(self):
  self.assertGreaterEqual(len(audit.audit(ROOT)['linked_sources']),28)
 def test_new_upstream_option_is_reported(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp)
   for folder in ['BloodborneLauncher','BloodborneLauncher.Native']:
    src=ROOT/'launcher/windows'/folder;dst=root/'launcher/windows'/folder
    if folder.endswith('.Native'):
     shutil.copytree(src/'Backend',dst/'Backend');shutil.copyfile(src/'BloodborneLauncher.Native.csproj',dst/'BloodborneLauncher.Native.csproj')
    else:
     for sub in ['Core','Views']:shutil.copytree(src/sub,dst/sub,ignore=shutil.ignore_patterns('bin','obj'))
   source=root/'launcher/windows/BloodborneLauncher/Views/GraphicsPage.cs'
   with source.open('a',encoding='utf-8') as f:f.write('\n// Contract fixture\nIniToggle(icon, "New author option", null, "graphics_author_new");\n')
   self.assertIn('graphics_author_new',audit.audit(root)['unmapped_keys'])
 def test_project_links_sources_without_wpf(self):
  p=ET.parse(ROOT/'launcher/windows/BloodborneLauncher.Native/BloodborneLauncher.Native.csproj')
  self.assertEqual(p.find('.//UseWPF').text,'false')
  self.assertEqual(p.find('.//UseWindowsForms').text,'false')
  self.assertTrue(any('Core/Choices.cs' in n.get('Include','') for n in p.findall('.//Compile')))
 def test_no_per_frame_ui_tree_or_forced_collection(self):
  src=ROOT/'launcher/windows/BloodborneLauncher.Native'
  text='\n'.join(p.read_text() for p in src.rglob('*.cs') if p.name != 'NavigationMemoryReclaimer.cs')
  self.assertNotIn('GC.Collect(',text);self.assertNotIn('EmptyWorkingSet(',text)
  self.assertIn('RetainContents',text)
if __name__=='__main__':unittest.main()
