#!/usr/bin/env python3
"""Retire only verified obsolete source modules before the engine's source glob."""
from pathlib import Path
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]


def normalized_hash(path):
    return hashlib.sha256(path.read_bytes().replace(b'\r\n', b'\n')).hexdigest()


def prepare(root=ROOT):
    moves = []
    for revision in (104, 112):
        plan_file = root / f'tools/upstream-update-{revision}.json'
        if not plan_file.is_file():
            continue
        plan = json.loads(plan_file.read_text(encoding='utf-8'))
        for item in plan['retired']:
            relative = Path(item['path'])
            if relative.is_absolute() or '..' in relative.parts or relative.parts[0] != 'engine' or relative.parts[1] not in ('src', 'gpu', 'tests'):
                raise ValueError('Invalid obsolete-source path in the integration plan.')
            source = root / relative
            if not source.exists():
                continue
            if source.is_symlink() or normalized_hash(source) != item['sha256']:
                raise ValueError(f'Obsolete source has local edits: {relative}. Preserve and review it before building.')
            backup = root / f'engine/.source-migration/{revision}' / relative.relative_to('engine')
            if backup.exists() and normalized_hash(backup) != item['sha256']:
                raise ValueError(f'Existing source backup differs: {backup}. No source files were moved.')
            moves.append((source, backup))
    for source, backup in moves:
        backup.parent.mkdir(parents=True, exist_ok=True)
        if not backup.exists():
            shutil.copy2(source, backup)
        source.unlink()
    print(f'Upstream source migration ready: {len(moves)} obsolete modules archived; local runtime data preserved.')


if __name__ == '__main__':
    try:
        prepare()
    except (OSError, ValueError, KeyError) as error:
        raise SystemExit(str(error))
