"""Cinematic voice selection, real synthetic MP4 metadata and safe source patching."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('cinematic_patch',ROOT/'tools/apply-cinematic-voice.py')
patcher=importlib.util.module_from_spec(spec);spec.loader.exec_module(patcher)

class CinematicPatch(unittest.TestCase):
 def fixture(self,root):
  source=root/patcher.SOURCE;source.parent.mkdir(parents=True,exist_ok=True)
  data=(ROOT/'tests/better_kbm/movie_voice/fixture/avplayer_source.cpp').read_bytes();source.write_bytes(data)
  header=root/patcher.HEADER;shutil.copyfile(ROOT/'engine'/patcher.HEADER,header)
  manifest={'version':47,'launcher_source_revision':'d901fbd2ea4bea114b37c26a2a9278d1e97f7b46',
            'unrelated':{'preserve':True},'files':{patcher.SOURCE:{'before':None,'after':patcher.digest(data)}}}
  (root/'better-kbm-manifest.json').write_text(json.dumps(manifest))
  return source

 def test_patch_retains_upstream_identity_and_updates_only_its_hashes(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=Path(temporary);source=self.fixture(root);patcher.apply(root)
   source_text=source.read_text();manifest=json.loads((root/'better-kbm-manifest.json').read_text())
   self.assertIn('CinematicVoice::Select(tracks, requested_voice',source_text)
   self.assertIn('BB_VOICE_LANGUAGE',source_text)
   self.assertNotIn('BB_TEXT_LANGUAGE',source_text)
   self.assertEqual(manifest['version'],48);self.assertEqual(manifest['unrelated'],{'preserve':True})
   self.assertEqual(manifest['launcher_source_revision'],'d901fbd2ea4bea114b37c26a2a9278d1e97f7b46')
   self.assertEqual(manifest['files'][patcher.SOURCE]['after'],patcher.digest(source.read_bytes()))
   before=(root/'better-kbm-manifest.json').read_bytes();patcher.apply(root)
   self.assertEqual((root/'better-kbm-manifest.json').read_bytes(),before)
   # Git autocrlf must not repeatedly increment the integration version.
   source.write_bytes(source.read_bytes().replace(b'\n',b'\r\n'));patcher.apply(root)
   self.assertEqual((root/'better-kbm-manifest.json').read_bytes(),before)

 def test_unregistered_local_changes_are_not_overwritten(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=Path(temporary);source=self.fixture(root);source.write_text(source.read_text()+'// local edit\n')
   before=source.read_bytes();manifest=(root/'better-kbm-manifest.json').read_bytes()
   with self.assertRaisesRegex(RuntimeError,'does not match'):patcher.apply(root)
   self.assertEqual(source.read_bytes(),before);self.assertEqual((root/'better-kbm-manifest.json').read_bytes(),manifest)

 def test_revision_98_header_can_be_upgraded_once(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=Path(temporary);source=self.fixture(root);patcher.apply(root)
   path=root/'better-kbm-manifest.json';manifest=json.loads(path.read_text())
   manifest['cinematic_voice_patch']=1
   manifest['files'][patcher.HEADER]['after']=patcher.PREVIOUS_HEADER
   path.write_text(json.dumps(manifest));patcher.apply(root)
   upgraded=json.loads(path.read_text())
   self.assertEqual(upgraded['version'],49)
   self.assertEqual(upgraded['cinematic_voice_patch'],2)
   self.assertEqual(upgraded['files'][patcher.HEADER]['after'],patcher.digest((root/patcher.HEADER).read_bytes()))
   before=path.read_bytes();patcher.apply(root);self.assertEqual(path.read_bytes(),before)

 def test_new_upstream_layout_is_rejected_before_writing(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=Path(temporary);source=self.fixture(root);source.write_text('// changed upstream source\n')
   path=root/'better-kbm-manifest.json';manifest=json.loads(path.read_text());manifest['files'][patcher.SOURCE]['after']=patcher.digest(source.read_bytes());path.write_text(json.dumps(manifest))
   before=path.read_bytes()
   with self.assertRaisesRegex(RuntimeError,'source layout'):patcher.apply(root)
   self.assertEqual(source.read_text(),'// changed upstream source\n');self.assertEqual(path.read_bytes(),before)

class CinematicSelection(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  compiler=shutil.which('g++') or shutil.which('clang++')
  if not compiler:raise unittest.SkipTest('C++20 compiler not installed')
  cls.temp=tempfile.TemporaryDirectory();cls.binary=Path(cls.temp.name)/('movie-voice.exe' if os.name=='nt' else 'movie-voice')
  subprocess.run([compiler,'-std=c++20','-Wall','-Wextra','-Wconversion','-Wsign-conversion','-Werror',
    '-I'+str(ROOT/'engine/gpu/shadps4/core/libraries/avplayer'),str(ROOT/'tests/better_kbm/movie_voice/test_cinematic_voice.cpp'),'-o',str(cls.binary)],check=True,capture_output=True,text=True)
 @classmethod
 def tearDownClass(cls):cls.temp.cleanup()
 def test_supported_voice_languages_and_regional_fallbacks(self):
  result=subprocess.run([str(self.binary)],check=True,capture_output=True,text=True)
  self.assertIn('PASS cinematic voice selection',result.stdout)
 @unittest.skipUnless(shutil.which('ffmpeg') and shutil.which('ffprobe'),'FFmpeg tools are needed for real-container metadata')
 def test_actual_mp4_audio_metadata_respects_voice_selection(self):
  with tempfile.TemporaryDirectory() as temporary:
   movie=Path(temporary)/'synthetic-intro.mp4'
   command=['ffmpeg','-v','error','-y','-f','lavfi','-i','color=c=black:s=16x16:d=0.12',
      '-f','lavfi','-i','anullsrc=r=48000:cl=mono','-map','0:v:0']
   for _ in range(8):command+=['-map','1:a:0']
   command+=['-t','0.12','-c:v','libx264','-c:a','aac']
   languages=['jpn','eng','fra','ita','deu','spa','spa','por']
   titles=['Japanese','English','French','Italian','German','Spanish Spain','Spanish Latin America','Portuguese Brazil']
   for i,(language,title) in enumerate(zip(languages,titles)):
    command+=['-metadata:s:a:'+str(i),'language='+language,'-metadata:s:a:'+str(i),'handler_name='+title]
   command+=[str(movie)];subprocess.run(command,check=True,capture_output=True,text=True)
   probe=json.loads(subprocess.run(['ffprobe','-v','error','-select_streams','a','-show_streams','-of','json',str(movie)],check=True,capture_output=True,text=True).stdout)
   tracks=[str(s['index'])+'|'+s.get('tags',{}).get('language','')+'|'+s.get('tags',{}).get('handler_name','') for s in probe['streams']]
   for voice,ordinal in [('1',1),('20',6),('3',5)]:
    result=subprocess.run([str(self.binary),voice,*tracks],check=True,capture_output=True,text=True)
    self.assertEqual(int(result.stdout.split()[0]),probe['streams'][ordinal]['index'])

if __name__=='__main__':unittest.main()
