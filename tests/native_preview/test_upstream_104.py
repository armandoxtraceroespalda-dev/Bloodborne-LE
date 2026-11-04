"""Exercise source retirement and verified provider downloads without network access."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


MIGRATION = module('upstream_migration', 'prepare-upstream-update.py')
SDK = module('fsr_sdk_fetch', 'fetch-fsr-sdk.py')


class Upstream104(unittest.TestCase):
    def setup_retirement(self, root):
        (root / 'tools').mkdir()
        source = root / 'engine/src/runtime_old.c'
        source.parent.mkdir(parents=True)
        source.write_bytes(b'old source\r\n')
        plan = {'retired': [{'path': 'engine/src/runtime_old.c', 'sha256': hashlib.sha256(b'old source\n').hexdigest()}]}
        (root / 'tools/upstream-update-104.json').write_text(json.dumps(plan))
        return source

    def test_obsolete_modules_are_archived_once_without_touching_saves(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = self.setup_retirement(root)
            save = root / 'engine/user/save.dat'
            save.parent.mkdir()
            save.write_bytes(b'player save')
            MIGRATION.prepare(root)
            self.assertFalse(source.exists())
            self.assertEqual((root / 'engine/.source-migration/104/src/runtime_old.c').read_bytes(), b'old source\r\n')
            MIGRATION.prepare(root)
            self.assertEqual(save.read_bytes(), b'player save')

    def test_locally_modified_obsolete_source_is_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = self.setup_retirement(root)
            source.write_bytes(b'local edit')
            with self.assertRaisesRegex(ValueError, 'local edits'):
                MIGRATION.prepare(root)
            self.assertEqual(source.read_bytes(), b'local edit')
            self.assertFalse((root / 'engine/.source-migration').exists())

    def test_sdk_download_validates_hash_and_keeps_custom_runtime(self):
        payload = b'official test runtime'
        files = {'test.dll': ('test.dll', len(payload), hashlib.sha256(payload).hexdigest())}
        with tempfile.TemporaryDirectory() as directory, patch.dict(SDK.os.environ, {'BB_FSR_SDK_DIR': ''}), patch.object(SDK, 'FILES', files), patch.object(SDK.urllib.request, 'urlopen', return_value=io.BytesIO(payload)):
            engine = Path(directory)
            SDK.fetch(engine)
            self.assertEqual((engine / 'fsr_sdk/test.dll').read_bytes(), payload)
            (engine / 'fsr_sdk/test.dll').write_bytes(b'custom runtime')
            SDK.fetch(engine)
            self.assertEqual((engine / 'fsr_sdk/test.dll').read_bytes(), b'custom runtime')

    def test_bad_download_is_not_installed(self):
        files = {'test.dll': ('test.dll', 3, hashlib.sha256(b'yes').hexdigest())}
        with tempfile.TemporaryDirectory() as directory, patch.dict(SDK.os.environ, {'BB_FSR_SDK_DIR': ''}), patch.object(SDK, 'FILES', files), patch.object(SDK.urllib.request, 'urlopen', return_value=io.BytesIO(b'bad')):
            engine = Path(directory)
            with self.assertRaisesRegex(ValueError, 'mismatch'):
                SDK.fetch(engine)
            self.assertFalse((engine / 'fsr_sdk/test.dll').exists())
            self.assertFalse((engine / 'fsr_sdk/test.dll.download').exists())


if __name__ == '__main__':
    unittest.main()
