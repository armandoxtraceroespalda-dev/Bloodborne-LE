#!/usr/bin/env python3
"""Return an absolute include path with the directory-entry spelling on disk."""
import os
from pathlib import Path
import sys

def canonical_build_path(value):
    path = Path(value).resolve(strict=True)
    # Windows accepts any case when opening a path, but Clang diagnoses a
    # forced include whose spelling differs from the actual directory entries.
    current = Path(path.anchor)
    for component in path.parts[1:]:
        with os.scandir(current) as entries:
            names = [entry.name for entry in entries if entry.name.casefold() == component.casefold()]
        if component in names:
            spelling = component
        elif len(names) == 1:
            spelling = names[0]
        else:
            raise FileNotFoundError('Could not resolve the on-disk spelling of ' + str(path))
        current = current / spelling
    return current.as_posix()

if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: canonical-build-path.py PATH')
    print(canonical_build_path(sys.argv[1]))
