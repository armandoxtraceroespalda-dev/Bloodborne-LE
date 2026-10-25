"""Check default migration, release retirement and transactional upgrades."""
from pathlib import Path
import importlib.util
import json
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('upgrade78',ROOT/'tools/apply-movement-options.py')
upgrade=importlib.util.module_from_spec(spec);spec.loader.exec_module(upgrade)


class MovementOptions(unittest.TestCase):
    def fixture(self,root):
        header='''int degrees_per_second=1200, release_delay_ms=0;
std::int64_t at = 0, active_at = 0;
void Apply(bool sprint, int x, int y) {
    if (sprint && !x && !y) {
        keep_going();
    } else if (x || y) state.active_at=now_us;
    const double target = 0;
bool reversal = state.target_x*x < 0 || state.target_y*y < 0;
if (!sprint || (!x && !y)) {
}
}
'''
        files={upgrade.previous.HEADER:header,
            upgrade.previous.BINDINGS:upgrade.previous.OLD_WALK+'\n{settings.stick_turn_rate,settings.stick_release_delay_ms,settings.stick_circular}',
            'gpu/shim/bbport_settings.h':'std::atomic<int> stick_turn_rate{1200},stick_release_delay_ms{60};',
            'gpu/shim/bbport_settings.cpp':'else if(key=="stick_release_delay_ms"){v.stick_release_delay_ms=std::clamp(i,0,200);\nput("stick_release_delay_ms",std::to_string(v.stick_release_delay_ms.load()));'}
        manifest={'version':41,'launcher_source_revision':'keep','files':{}}
        for name,text in files.items():
            path=root/'engine'/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(text)
            manifest['files'][name]={'before':None,'after':upgrade.previous.digest(path.read_bytes())}
        (root/'engine/better-kbm-manifest.json').write_text(json.dumps(manifest))
        (root/'engine/lantern.ini').write_text('stick_turn_rate=2300\nstick_release_delay_ms=60\n')

    def test_retired_buffering_defaults_and_idempotence(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root);upgrade.apply(root)
            self.assertEqual(upgrade.apply(root),{})
            header=(root/'engine'/upgrade.previous.HEADER).read_text()
            self.assertNotIn('active_at',header);self.assertNotIn('if (sprint',header)
            self.assertIn('degrees_per_second=800',header)
            self.assertIn('bool /*sprint*/,',header)
            settings=(root/'engine/gpu/shim/bbport_settings.h').read_text()
            self.assertIn('stick_turn_rate{800}',settings);self.assertIn('stick_release_delay_ms{0}',settings)
            io=(root/'engine/gpu/shim/bbport_settings.cpp').read_text()
            self.assertIn('stick_release_delay_ms=0;',io);self.assertIn('put("stick_release_delay_ms","0")',io)
            self.assertEqual((root/'engine/lantern.ini').read_text(),'stick_turn_rate=2300\nstick_release_delay_ms=60\n')
            manifest=json.loads((root/'engine/better-kbm-manifest.json').read_text())
            self.assertEqual(manifest['version'],41);self.assertEqual(manifest['launcher_source_revision'],'keep')
            for name,item in manifest['files'].items():self.assertEqual(upgrade.previous.digest((root/'engine'/name).read_bytes()),item['after'])

    def test_unknown_serializer_does_not_apply_partial_upgrade(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);self.fixture(root)
            path=root/'engine/gpu/shim/bbport_settings.cpp';path.write_text('unknown serialization')
            manifest=root/'engine/better-kbm-manifest.json';data=json.loads(manifest.read_text())
            data['files']['gpu/shim/bbport_settings.cpp']['after']=upgrade.previous.digest(path.read_bytes());manifest.write_text(json.dumps(data))
            before={p:p.read_bytes() for p in root.rglob('*') if p.is_file()}
            with self.assertRaises(ValueError):upgrade.apply(root)
            self.assertEqual(before,{p:p.read_bytes() for p in before})


if __name__=='__main__':unittest.main()
