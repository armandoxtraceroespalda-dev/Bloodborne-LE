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

def add_ignore_rules(path, rules):
    """Append without recreating a hidden Windows file or changing existing bytes."""
    exists = path.exists()
    original = path.read_bytes() if exists else b''
    existing = original.decode('utf-8-sig').splitlines()
    missing = [rule for rule in rules if rule not in existing]
    if not missing:
        return
    newline = b'\r\n' if b'\r\n' in original else b'\n'
    prefix = newline if original and not original.endswith((b'\r', b'\n')) else b''
    addition = prefix + newline.join(rule.encode('utf-8') for rule in missing) + newline
    # FileMode.Create/CREATE_ALWAYS cannot reopen an existing hidden file on
    # Windows. OPEN_EXISTING keeps its attributes, BOM, line endings and rules.
    with path.open('r+b' if exists else 'xb') as stream:
        stream.seek(0, os.SEEK_END)
        stream.write(addition)

def install(published, destination, previous_preview, support=None):
    published, destination, previous_preview = map(Path, (published, destination, previous_preview))
    support = Path(support) if support is not None else destination
    if not (published / EXE).is_file():
        raise RuntimeError('The published native launcher is missing: ' + str(published / EXE))
    powershell = shutil.which('powershell.exe')
    if powershell:
        query = 'Get-Process | Where-Object { $_.ProcessName -in @("Bloodborne Launcher","Bloodborne Native Preview","Lantern Engine") } | Select-Object -ExpandProperty ProcessName'
        running = subprocess.run([powershell, '-NoProfile', '-Command', query], capture_output=True, text=True, check=True)
        if running.stdout.strip():
            raise RuntimeError('Close all launcher windows before installing: ' + running.stdout.strip())
    destination.mkdir(parents=True, exist_ok=True)
    support.mkdir(parents=True, exist_ok=True)
    backup = support / 'migration-backup'
    backup.mkdir(exist_ok=True)
    previous = destination / EXE
    saved = backup / (EXE + '.previous')
    if previous.exists() and not saved.exists():
        shutil.copy2(previous, saved)
    # The publish output contains fonts/notices, never extracted gui or user settings.
    for source in published.rglob('*'):
        relative = source.relative_to(published)
        if not source.is_file() or relative.parts[0] == 'gui' or relative == Path(EXE): continue
        atomic_copy(source, support / relative)
    atomic_copy(published / EXE, destination / EXE)
    # Reuse the already tested artwork, only filling missing files.
    legacy_folders = []
    if support != destination:
        for directory in [destination / 'LauncherApp', destination / 'NativeLauncherApp', previous_preview]:
            if directory.resolve() not in {support.resolve(), destination.resolve()} and directory not in legacy_folders:
                legacy_folders.append(directory)
    for gui in [support / 'gui', previous_preview / 'gui'] + [folder / 'gui' for folder in legacy_folders]:
        if gui.is_dir() and gui.resolve() != (destination / 'gui').resolve():
            for source in gui.rglob('*'):
                target = destination / 'gui' / source.relative_to(gui)
                if source.is_file() and not target.exists(): atomic_copy(source, target)
    retired_paths = [destination / 'Lantern Engine.exe', previous_preview / 'Bloodborne Native Preview.exe']
    if support != destination: retired_paths += [support / EXE, support / 'Lantern Engine.exe']
    for old in retired_paths:
        if old.is_file():
            retired = backup / (old.name + '.retired')
            suffix = 1
            while retired.exists():
                retired = backup / (old.name + '.retired.' + str(suffix)); suffix += 1
            shutil.move(str(old), str(retired))
    if support != destination:
        # Retain every old file, including custom settings and artwork, but remove
        # the obsolete root-level folders. Never move the active source directory.
        for folder in legacy_folders:
            if not folder.is_dir(): continue
            retired = backup / ('Legacy-' + folder.name)
            suffix = 1
            while retired.exists():
                retired = backup / ('Legacy-' + folder.name + '-' + str(suffix)); suffix += 1
            for name in [EXE, 'Bloodborne Native Preview.exe', 'Lantern Engine.exe']:
                old = folder / name
                if old.is_file():
                    archived_exe = old.with_name(old.name + '.retired')
                    counter = 1
                    while archived_exe.exists():
                        archived_exe = old.with_name(old.name + '.retired-' + str(counter)); counter += 1
                    old.rename(archived_exe)
            shutil.move(str(folder), str(retired))
    (support / '.native-default').write_text(json.dumps({'launcher': EXE, 'revision': 97}) + '\n')
    if support != destination:
        # Keep existing repository rules and exclude the generated local outputs.
        rules = ['/' + EXE, '/gui/', '/engine/lantern.ini']
        if support == destination / 'launcher':
            # The source folder stays tracked. Ignore only generated support data.
            rules += ['/launcher/' + name for name in ['fonts/', 'third-party/', 'migration-backup/', '.native-default',
                      'upstream-audit.json', 'native-preview-error.txt', 'native-preview-metrics.json',
                      'memory-comparison.csv', 'visual-reference/']]
            rules += ['/launcher/' + str(p.relative_to(published)).replace('\\', '/') for p in published.iterdir() if p.is_file() and p.name != EXE]
        add_ignore_rules(destination / '.gitignore', rules)
        if os.name == 'nt':
            import ctypes
            for name in ['.git', '.github', '.gitignore', '.gitattributes']:
                path = destination / name
                if not path.exists(): continue
                attributes = ctypes.windll.kernel32.GetFileAttributesW(str(path))
                if attributes != -1: ctypes.windll.kernel32.SetFileAttributesW(str(path), attributes | 2)
    print('Installed: ' + str(destination / EXE))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published', required=True)
    parser.add_argument('--destination', required=True)
    parser.add_argument('--previous-preview', required=True)
    parser.add_argument('--support')
    args = parser.parse_args()
    install(args.published, args.destination, args.previous_preview, args.support)
