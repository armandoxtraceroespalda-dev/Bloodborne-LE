"""Verify the focused engine upgrade preserves unrelated revisions and settings."""
from pathlib import Path
import copy
import importlib.util
import json
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('movement_upgrade', ROOT/'tools/apply-all-movement-turning.py')
upgrade = importlib.util.module_from_spec(spec)
spec.loader.exec_module(upgrade)


class MovementUpgrade(unittest.TestCase):
    def fixture(self, root):
        files = {
            upgrade.HEADER: 'if (sprint && !x && !y) {}\nbool reversal = state.target_x*x < 0 || state.target_y*y < 0;\nif (!sprint || (!x && !y)) {\n}\n',
            upgrade.BINDINGS: 'keep character creation fixes\n' + upgrade.OLD_WALK + '\nkeep mouse bindings\n',
        }
        manifest = {'version':41,'launcher_source_revision':'keep-upstream','custom':'preserve',
            'files':{name:{'before':None,'after':upgrade.digest(text.encode())} for name,text in files.items()}}
        for name,text in files.items():
            path=root/'engine'/name; path.parent.mkdir(parents=True,exist_ok=True); path.write_text(text)
        path=root/'engine/better-kbm-manifest.json'; path.write_text(json.dumps(manifest))
        legacy=root/'tests/better_kbm/test_sprint_turn.cpp'; legacy.parent.mkdir(parents=True,exist_ok=True)
        legacy.write_text('assert(immediate.y==0&&!state.active);\n    SprintTurn::Apply(state,0,-1,true,1020000);\n')
        (root/'engine/lantern.ini').write_text('stick_turn_rate=1200\nstick_release_delay_ms=0\n')
        return manifest

    def test_idempotent_and_preserves_source_revision_and_settings(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); old=self.fixture(root)
            upgrade.apply(root)
            self.assertEqual(upgrade.apply(root), {})
            manifest=json.loads((root/'engine/better-kbm-manifest.json').read_text())
            self.assertEqual(manifest['version'],old['version'])
            self.assertEqual(manifest['launcher_source_revision'],'keep-upstream')
            self.assertEqual(manifest['custom'],'preserve')
            for name in old['files']:
                self.assertEqual(upgrade.digest((root/'engine'/name).read_bytes()),manifest['files'][name]['after'])
            text=(root/'engine'/upgrade.BINDINGS).read_text()
            self.assertIn('keep character creation fixes',text)
            self.assertIn('keep mouse bindings',text)
            self.assertIn(upgrade.NEW_WALK,text)
            self.assertEqual((root/'engine/lantern.ini').read_text(),'stick_turn_rate=1200\nstick_release_delay_ms=0\n')

    def test_unknown_source_writes_nothing(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); self.fixture(root)
            binding=root/'engine'/upgrade.BINDINGS
            binding.write_text('unrecognized local changes')
            before={p:p.read_bytes() for p in root.rglob('*') if p.is_file()}
            with self.assertRaises(ValueError): upgrade.apply(root)
            self.assertEqual(before,{p:p.read_bytes() for p in before})

    def test_unknown_walk_implementation_writes_nothing(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); self.fixture(root)
            binding=root/'engine'/upgrade.BINDINGS; binding.write_text('another implementation')
            manifest=root/'engine/better-kbm-manifest.json'; data=json.loads(manifest.read_text())
            data['files'][upgrade.BINDINGS]['after']=upgrade.digest(binding.read_bytes()); manifest.write_text(json.dumps(data))
            before={p:p.read_bytes() for p in root.rglob('*') if p.is_file()}
            with self.assertRaises(ValueError): upgrade.apply(root)
            self.assertEqual(before,{p:p.read_bytes() for p in before})

    def test_write_failure_restores_prior_sources(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp); self.fixture(root)
            before={p:p.read_bytes() for p in root.rglob('*') if p.is_file()}
            original=upgrade.atomic_write; calls=0
            def fail_once(path,data):
                nonlocal calls
                calls+=1
                if calls==2: raise OSError('simulated interrupted write')
                return original(path,data)
            upgrade.atomic_write=fail_once
            try:
                with self.assertRaises(OSError): upgrade.apply(root)
            finally: upgrade.atomic_write=original
            self.assertEqual(before,{p:p.read_bytes() for p in before})


if __name__=='__main__': unittest.main()
