#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Extend the existing keyboard turning filter without replacing engine revisions."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import tempfile

HEADER = 'gpu/shim/input/sprint_turn.h'
BINDINGS = 'gpu/shim/input/bindings.cpp'
OLD_WALK = '''            const double scale = kWalkPush/std::hypot(double(x),double(y));
            axes = {int(std::lround(x*127*scale)),int(std::lround(y*127*scale))};'''
NEW_WALK = '''            // Preserve the filtered direction while limiting slow-walk intensity.
            const double scale = (127*kWalkPush)/std::hypot(double(axes.x),double(axes.y));
            axes = {int(std::lround(axes.x*scale)),int(std::lround(axes.y*scale))};'''


def digest(data):
    return hashlib.sha256(data.replace(b'\r\n', b'\n')).hexdigest()


def replace_once(text, old, new, label):
    if new in text and old not in text:
        return text
    if text.count(old) != 1 or new in text:
        raise ValueError(f'Unrecognized {label}; no files were changed. Keep this engine source for review.')
    return text.replace(old, new, 1)


def plan(root):
    engine = root / 'engine'
    manifest_path = engine / 'better-kbm-manifest.json'
    manifest_raw = manifest_path.read_bytes()
    manifest = json.loads(manifest_raw)
    changes = {}
    for name in (HEADER, BINDINGS):
        path = engine / name
        original = path.read_bytes()
        if name not in manifest.get('files', {}):
            raise ValueError(f'{name} is absent from the engine manifest; no files were changed.')
        if digest(original) != manifest['files'][name]['after']:
            raise ValueError(f'Engine manifest does not match {name}; no files were changed.')
        text = original.decode('utf-8').replace('\r\n', '\n')
        if name == HEADER:
            # Keep the sprint-only key-release grace period and reversal test intact.
            for guard in ('state.target_x*x < 0 || state.target_y*y < 0',):
                if guard not in text:
                    raise ValueError(f'Unrecognized turning guard: {guard}; no files were changed.')
            text = replace_once(text, 'if (!sprint || (!x && !y)) {', 'if (!x && !y) {', 'turning filter')
            text = text.replace('// Round only compatible sprint turns, never an opposing-key reversal. Retain',
                '// Round compatible keyboard turns in every movement mode. Retain')
        else:
            text = replace_once(text, OLD_WALK, NEW_WALK, 'slow-walk direction scaling')
            text = text.replace('// Keep full directional input during ordinary sprint turns, while a',
                '// Keep filtered directional input in every keyboard movement mode; a')
        updated = text.encode('utf-8')
        if updated != original:
            changes[path] = (original, updated)
        manifest['files'][name]['after'] = digest(updated)
    if '// No key-release buffering.' not in (changes.get(engine/HEADER,(None,(engine/HEADER).read_bytes()))[1]).decode('utf-8'):
        manifest['keyboard_turning_scope'] = 'all-movement; immediate opposing reversals; sprint-only release grace'
    updated_manifest = (json.dumps(manifest, indent=2, ensure_ascii=False) + '\n').encode('utf-8')
    if manifest != json.loads(manifest_raw):
        changes[manifest_path] = (manifest_raw, updated_manifest)

    # The older test explicitly expected non-sprint input to reset the filter.
    # Update only that obsolete expectation if this historical fixture is present.
    legacy = root / 'tests/better_kbm/test_sprint_turn.cpp'
    if legacy.is_file():
        original = legacy.read_bytes()
        updated = original.replace(b'immediate.y==0&&!state.active', b'immediate.y==0&&state.active')
        if b'// Isolate the same-timestamp case' not in updated:
            updated = updated.replace(b'    SprintTurn::Apply(state,0,-1,true,1020000);',
                b'    state={}; // Isolate the same-timestamp case from the preceding movement.\n    SprintTurn::Apply(state,0,-1,true,1020000);')
        if original != updated:
            changes[legacy] = (original, updated)
    return changes


def atomic_write(path, data):
    fd, name = tempfile.mkstemp(prefix='.' + path.name + '.', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as output:
            output.write(data)
        os.replace(name, path)
    finally:
        Path(name).unlink(missing_ok=True)


def apply(root):
    changes = plan(root)  # Validate every source before writing anything.
    written = []
    try:
        for path, (_, updated) in changes.items():
            atomic_write(path, updated)
            written.append(path)
    except BaseException:
        for path in reversed(written):
            atomic_write(path, changes[path][0])
        raise
    return changes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        changed = apply(args.root)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, str(error) + '\n')
    print(f'All-movement turning ready: {len(changed)} files updated; existing engine revision and settings preserved.')


if __name__ == '__main__':
    main()
