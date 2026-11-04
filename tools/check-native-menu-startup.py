#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Check the native menu worker and its cache contract before an engine build."""
import contextlib
import io
from pathlib import Path
import sys
import unittest


def main():
    engine = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / 'engine'
    sys.path[:0] = [str(engine / 'tests'), str(engine / 'scripts')]
    suite = unittest.defaultTestLoader.loadTestsFromNames(('test_engine_assets', 'test_native_menu_startup'))
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        result = unittest.TextTestRunner(verbosity=1).run(suite)
    if not result.wasSuccessful():
        sys.stderr.write(output.getvalue())
        return 1
    print('Native menu startup: worker, menu generation, languages and cache checks passed.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
