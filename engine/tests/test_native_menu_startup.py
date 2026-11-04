# SPDX-License-Identifier: GPL-3.0-or-later
"""Exercise the real native menu worker and preparation entry point with local fixtures."""
from paths import ROOT
import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import engine_assets
import engine_features


class NativeMenuStartupTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game, self.cache, self.tool = self.root / 'game', self.root / 'cache', self.root / 'tool'
        self.sources = {}
        for name in ('menu/optionsetting.gfx', 'msg/engus/menu.msgbnd.dcx', 'msg/spaar/menu.msgbnd.dcx'):
            path = self.game / 'dvdroot_ps4' / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(('original fixture: ' + name).encode())
            self.sources[path] = path.read_bytes()
        self.tool.write_bytes(b'local generator fixture')
        self.calls = []
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        self.stack.enter_context(patch.object(engine_assets, 'verify_executable', return_value=engine_assets.IMAGE_SHA))
        self.stack.enter_context(patch.object(engine_features, 'verify_executable', return_value=engine_assets.IMAGE_SHA))
        self.stack.enter_context(patch.object(engine_features, 'prepare_effect_fixes', return_value=[]))
        self.stack.enter_context(patch.object(engine_assets.subprocess, 'run', self.generate))
        self.stack.enter_context(patch('menu_layout.expand_option_panels', side_effect=lambda data: data))
        self.stack.enter_context(patch('quit_menu.taller_list', return_value=None))
        self.stack.enter_context(patch('quit_menu.menu_texts', side_effect=lambda data, texts: data))
        self.stack.enter_context(patch('pc_localization.localize_bundle', side_effect=lambda data, language: data))
        self.stack.enter_context(patch.object(engine_assets, 'dcx_pack', side_effect=lambda data: data))
        self.stack.enter_context(patch.object(engine_assets, 'dcx_unpack', side_effect=lambda data: data))

    def generate(self, args, **kwargs):
        self.calls.append((args[1], kwargs.get('env', os.environ).copy()))
        Path(args[3]).write_bytes(Path(args[2]).read_bytes() + b' | native PC ' + args[1].encode())
        return SimpleNamespace(returncode=0, stderr='')

    def worker(self, enabled):
        args = ['prepare_pc_menu_assets.py', str(self.game), str(self.root / 'eboot'), str(self.cache), str(self.tool), '0', '1' if enabled else '0']
        with patch.object(sys, 'argv', args):
            runpy.run_path(str(ROOT / 'scripts/prepare_pc_menu_assets.py'), run_name='__main__')
        return self.cache / 'pc-menus'

    def test_native_worker_publishes_movie_and_all_locales(self):
        folder = self.worker(True)
        self.assertEqual([kind for kind, _ in self.calls], ['movie', 'messages', 'messages'])
        movie = folder / 'dvdroot_ps4/menu/optionsetting.gfx'
        self.assertIn(b'native PC movie', movie.read_bytes())
        self.assertEqual(len(json.loads((folder / 'manifest.json').read_text())['files']), 3)
        self.assertTrue(all(path.read_bytes() == original for path, original in self.sources.items()))

    def test_native_worker_honors_disabled_menus_and_rebuilds_when_enabled(self):
        folder = self.worker(False)
        movie = folder / 'dvdroot_ps4/menu/optionsetting.gfx'
        original = self.game / 'dvdroot_ps4/menu/optionsetting.gfx'
        self.assertEqual(movie.read_bytes(), original.read_bytes())
        self.assertNotIn('movie', [kind for kind, _ in self.calls])
        self.worker(True)
        self.assertIn(b'native PC movie', movie.read_bytes())
        count = len(self.calls)
        self.worker(True)
        self.assertEqual(len(self.calls), count)

    def test_native_worker_preserves_inherited_coop_titles(self):
        titles = {'BB_PLATE_HOST_TITLE': '=Room host', 'BB_PLATE_GUEST_TITLE': '=Room guest'}
        with patch.dict(os.environ, titles):
            self.worker(True)
        self.assertTrue(all(env['BB_PLATE_HOST_TITLE'] == '=Room host' and env['BB_PLATE_GUEST_TITLE'] == '=Room guest' for _, env in self.calls))

    def test_python_entry_point_combines_menu_flag_and_explicit_titles(self):
        config = self.root / 'lantern.ini'
        config.write_text('pc_controls=1\nwidescreen=0\nui_4k=0\ningame_tweak_menus=1\nplate_host_title=Host\nplate_guest_title=Guest\n')
        out = self.root / 'out'
        out.mkdir()
        (out / 'bb-engine-assets').write_bytes(self.tool.read_bytes())
        result = engine_features.prepare(self.game, out, self.root / 'data', config)
        self.assertIn('BB_PC_MENU_ASSETS', result)
        self.assertTrue(Path(result['BB_PC_MENU_ASSETS'], 'dvdroot_ps4/menu/optionsetting.gfx').is_file())
        self.assertTrue(all(env['BB_PLATE_HOST_TITLE'] == '=Host' and env['BB_PLATE_GUEST_TITLE'] == '=Guest' for _, env in self.calls))

    def test_explicit_titles_do_not_leak_into_parent_environment(self):
        with patch.dict(os.environ, {'BB_PLATE_HOST_TITLE': '=Inherited', 'BB_PLATE_GUEST_TITLE': '=Guest'}):
            engine_assets.prepare_menu_assets(self.game, self.root / 'eboot', self.cache, self.tool,
                tweak_menus=True, host_title='Local', guest_title='Visitor')
            self.assertEqual(os.environ['BB_PLATE_HOST_TITLE'], '=Inherited')
            self.assertEqual(os.environ['BB_PLATE_GUEST_TITLE'], '=Guest')
        self.assertTrue(all(env['BB_PLATE_HOST_TITLE'] == '=Local' for _, env in self.calls))


if __name__ == '__main__':
    unittest.main()
