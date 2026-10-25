"""Exercise engine build orchestration without a Vulkan compiler or real game data."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]

class InPlaceEngineBuild(unittest.TestCase):
 def test_build_stays_in_root_engine_and_preserves_existing_data(self):
  with tempfile.TemporaryDirectory() as temporary:
   base=Path(temporary);root=base/'project with spaces';engine=root/'engine';tools=root/'tools';fake=base/'fake-bin';prefix=base/'prefix'
   for path in [tools,engine/'gpu',engine/'tools',engine/'user',fake,prefix/'include/nlohmann']:path.mkdir(parents=True,exist_ok=True)
   (prefix/'include/expat.h').write_text('fixture');(prefix/'include/nlohmann/json.hpp').write_text('fixture')
   (engine/'gpu/win_compat.h').write_text('// fixture')
   (engine/'source.cpp').write_text('// source remains unchanged\n')
   digest=hashlib.sha256((engine/'source.cpp').read_bytes()).hexdigest()
   manifest={'integration':'fixture-native','version':987,'launcher_source_revision':'a'*40,'upstream_release':'fixture release',
             'files':{'source.cpp':{'before':None,'after':digest}}}
   (engine/'better-kbm-manifest.json').write_text(json.dumps(manifest))
   shutil.copy2(ROOT/'tools/build-better-kbm.sh',tools/'build-better-kbm.sh')
   shutil.copy2(ROOT/'tools/verify-engine-sources.py',tools/'verify-engine-sources.py')
   shutil.copy2(ROOT/'tools/canonical-build-path.py',tools/'canonical-build-path.py')
   (tools/'deploy-runtime-dlls.py').write_text('from pathlib import Path\nimport sys\nassert Path(sys.argv[1]).name=="out"\n')
   (tools/'build-launcher-only.sh').write_text('#!/usr/bin/env bash\nset -e\nroot="$(cd -- "$(dirname -- "$0")/.." && pwd)"\nprintf native > "$root/Bloodborne Launcher.exe"\n')
   (engine/'build.sh').write_text('#!/usr/bin/env bash\nset -e\nmkdir -p out\nfor p in bb-probe bb-engine-assets bb-start bb-shader-lab; do printf fixture > "out/$p.exe"; done\n')
   (engine/'tools/fetch_dlss.sh').write_text('#!/usr/bin/env bash\nexit 0\n')
   for name in ['cmake','ninja','clang','curl']:
    path=fake/name;path.write_text('#!/usr/bin/env bash\nexit 0\n');path.chmod(0o755)
   path=fake/'cygpath';path.write_text('#!/usr/bin/env bash\nprintf "%s\\n" "${@: -1}"\n');path.chmod(0o755)
   previous=base/'previous-engine';(previous/'user').mkdir(parents=True)
   (previous/'lantern.ini').write_text('output_res=2560x1440\n')
   (previous/'user/save.dat').write_bytes(b'original save')
   (previous/'user/existing.dat').write_bytes(b'older copy')
   (engine/'user/existing.dat').write_bytes(b'keep current copy')
   env=dict(os.environ,MSYSTEM='CLANG64',MINGW_PREFIX=str(prefix),BB_PREVIOUS_ENGINE=str(previous),
            BB_ENGINE_TARGET=str(base/'unwanted-sibling'),PATH=str(fake)+os.pathsep+os.environ['PATH'])
   result=subprocess.run(['bash',str(tools/'build-better-kbm.sh')],env=env,text=True,capture_output=True)
   self.assertEqual(result.returncode,0,result.stdout+'\n'+result.stderr)
   self.assertTrue((root/'Bloodborne Launcher.exe').is_file())
   self.assertEqual((engine/'lantern.ini').read_text(),'output_res=2560x1440\n')
   self.assertEqual((engine/'user/save.dat').read_bytes(),b'original save')
   self.assertEqual((engine/'user/existing.dat').read_bytes(),b'keep current copy')
   self.assertEqual((previous/'user/save.dat').read_bytes(),b'original save')
   self.assertEqual(hashlib.sha256((engine/'source.cpp').read_bytes()).hexdigest(),digest)
   self.assertFalse((base/'unwanted-sibling').exists())
   self.assertIn('Engine folder: '+str(engine),result.stdout)
   stamp=json.loads((engine/'out/better-kbm-build.json').read_text())
   self.assertEqual(stamp['version'],987);self.assertEqual(stamp['upstream_release'],'fixture release')
   self.assertEqual(stamp['upstream'],'a'*40)
   # A rebuild may copy missing settings, but never replace an existing local INI.
   (previous/'lantern.ini').write_text('output_res=999x999\n')
   again=subprocess.run(['bash',str(tools/'build-better-kbm.sh')],env=env,text=True,capture_output=True)
   self.assertEqual(again.returncode,0,again.stderr)
   self.assertEqual((engine/'lantern.ini').read_text(),'output_res=2560x1440\n')
   # Source validation remains mandatory and runs before invoking the build.
   (engine/'source.cpp').write_text('// unexpected change\n')
   failed=subprocess.run(['bash',str(tools/'build-better-kbm.sh')],env=env,text=True,capture_output=True)
   self.assertNotEqual(failed.returncode,0);self.assertIn('Engine source verification failed',failed.stderr)

if __name__=='__main__':unittest.main()
