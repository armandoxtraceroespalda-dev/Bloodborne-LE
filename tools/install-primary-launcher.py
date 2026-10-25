#!/usr/bin/env python3
"""Install the native primary launcher, preserving gui and backing up old entry points."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

EXE = 'Bloodborne Launcher.exe'

def atomic_copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + '.installing')
    try:
        shutil.copy2(source, temporary)
        os.replace(temporary, target)
    finally:
        temporary.unlink(missing_ok=True)

def install(published, destination, previous_preview):
    published, destination, previous_preview = map(Path, (published, destination, previous_preview))
    if not (published / EXE).is_file():
        raise RuntimeError('The published native launcher is missing: ' + str(published / EXE))
    powershell = shutil.which('powershell.exe')
    if powershell:
        query = 'Get-Process | Where-Object { $_.ProcessName -in @("Bloodborne Launcher","Bloodborne Native Preview","Lantern Engine") } | Select-Object -ExpandProperty ProcessName'
        running = subprocess.run([powershell, '-NoProfile', '-Command', query], capture_output=True, text=True, check=True)
        if running.stdout.strip():
            raise RuntimeError('Close all launcher windows before installing: ' + running.stdout.strip())
    destination.mkdir(parents=True, exist_ok=True)
    backup = destination / 'migration-backup'
    backup.mkdir(exist_ok=True)
    previous = destination / EXE
    saved = backup / (EXE + '.previous')
    if previous.exists() and not saved.exists():
        shutil.copy2(previous, saved)
    # The publish output contains fonts/notices, never extracted gui or user settings.
    for source in published.rglob('*'):
        relative = source.relative_to(published)
        if not source.is_file() or relative.parts[0] == 'gui' or relative == Path(EXE): continue
        atomic_copy(source, destination / relative)
    atomic_copy(published / EXE, destination / EXE)
    # Reuse the already tested artwork, only filling missing files.
    gui = previous_preview / 'gui'
    if gui.is_dir():
        for source in gui.rglob('*'):
            target = destination / 'gui' / source.relative_to(gui)
            if source.is_file() and not target.exists(): atomic_copy(source, target)
    for old in [destination / 'Lantern Engine.exe', previous_preview / 'Bloodborne Native Preview.exe']:
        if old.is_file():
            retired = backup / (old.name + '.retired')
            suffix = 1
            while retired.exists():
                retired = backup / (old.name + '.retired.' + str(suffix)); suffix += 1
            shutil.move(str(old), str(retired))
    (destination / '.native-default').write_text(json.dumps({'launcher': EXE, 'revision': 92}) + '\n')
    print('Installed: ' + str(destination / EXE))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published', required=True)
    parser.add_argument('--destination', required=True)
    parser.add_argument('--previous-preview', required=True)
    args = parser.parse_args()
    install(args.published, args.destination, args.previous_preview)
