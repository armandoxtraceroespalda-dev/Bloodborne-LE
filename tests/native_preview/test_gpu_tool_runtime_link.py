"""Keep GPU-only tools linkable without attaching the game runtime."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
COMPILER = shutil.which('clang++') or shutil.which('g++') or shutil.which('c++')


class GpuToolRuntimeLink(unittest.TestCase):
    @unittest.skipUnless(COMPILER, 'a C++ compiler is required')
    def test_controller_lookup_links_without_the_game_runtime(self):
        with tempfile.TemporaryDirectory(prefix='gpu tool link ') as directory:
            work = Path(directory)
            caller = work / 'caller.cpp'
            caller.write_text('''#include <cstdint>
extern "C" int runtime_pad_button(uint32_t, int);
int main() {
    const uint32_t buttons[] = {0u, 1u, 0x1000u, 0xFFFFFFFFu};
    for (uint32_t button : buttons)
        for (int side = 0; side < 2; ++side)
            if (runtime_pad_button(button, side) != -1) return 1;
    return 0;
}
''', encoding='utf-8')
            exe = work / ('caller.exe' if os.name == 'nt' else 'caller')
            result = subprocess.run([
                COMPILER, '-std=c++17', str(caller),
                str(ROOT / 'engine/tests/test_gpu_runtime_stubs.cpp'),
                '-o', str(exe)
            ], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(subprocess.run([str(exe)]).returncode, 0)


if __name__ == '__main__':
    unittest.main()
