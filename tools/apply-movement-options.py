#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Set keyboard turning defaults and retire release buffering, preserving engine APIs."""
from pathlib import Path
import importlib.util
import json
import re
import argparse

spec = importlib.util.spec_from_file_location('movement77', Path(__file__).with_name('apply-all-movement-turning.py'))
previous = importlib.util.module_from_spec(spec)
spec.loader.exec_module(previous)
MARKER = '// No key-release buffering.'


def plan(root):
    changes = previous.plan(root)
    manifest_path = root/'engine/better-kbm-manifest.json'
    manifest = json.loads(changes.get(manifest_path, (None,manifest_path.read_bytes()))[1])
    def edit(name, transform):
        path = root/'engine'/name
        original = path.read_bytes()
        current = changes.get(path,(None,original))[1]
        if name not in manifest['files'] or previous.digest(current) != manifest['files'][name]['after']:
            raise ValueError(f'Engine manifest mismatch in {name}; no files were changed.')
        updated = transform(current.decode('utf-8').replace('\r\n','\n')).encode()
        if updated != original: changes[path] = (original,updated)
        manifest['files'][name]['after'] = previous.digest(updated)

    def header(text):
        if MARKER not in text:
            start = text.index('    if (sprint && !x && !y')
            end = text.index('    const double target',start)
            text = text[:start] + '    '+MARKER+' Releasing movement stops immediately.\n' + text[end:]
            text = previous.replace_once(text,'bool sprint,','bool /*sprint*/,','turning signature')
        text = text.replace('at = 0, active_at = 0','at = 0')
        text,count = re.subn(r'(degrees_per_second\s*=\s*)(?:900|1200|800)\b',r'\g<1>800',text)
        if count != 1: raise ValueError('Unrecognized movement default; no files were changed.')
        return text

    def settings(text):
        text,count = re.subn(r'(stick_turn_rate\{)(?:900|1200|800)(\})',r'\g<1>800\2',text)
        if count != 1: raise ValueError('Unrecognized settings default; no files were changed.')
        text,count = re.subn(r'(stick_release_delay_ms\{)\d+(\})',r'\g<1>0\2',text)
        if count != 1: raise ValueError('Unrecognized legacy release default; no files were changed.')
        return text

    def io(text):
        text,count = re.subn(r'(key\s*==\s*"stick_release_delay_ms"\s*\)\s*\{\s*v\.stick_release_delay_ms\s*=\s*)[^;]+;',r'\g<1>0;',text)
        if count != 1: raise ValueError('Unrecognized legacy release parser; no files were changed.')
        old='put("stick_release_delay_ms",std::to_string(v.stick_release_delay_ms.load()));'
        return previous.replace_once(text,old,'put("stick_release_delay_ms","0");','legacy release serializer')

    edit(previous.HEADER,header)
    edit(previous.BINDINGS,lambda text:previous.replace_once(text,
        '{settings.stick_turn_rate,settings.stick_release_delay_ms,settings.stick_circular}',
        '{settings.stick_turn_rate,0,settings.stick_circular}','movement configuration'))
    edit('gpu/shim/bbport_settings.h',settings)
    edit('gpu/shim/bbport_settings.cpp',io)
    manifest['keyboard_turning_scope']='all-movement; immediate opposing reversals; no release buffering'
    manifest['keyboard_turning_default']=800
    changes[manifest_path]=(manifest_path.read_bytes(),(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n').encode())

    # Historical tests of the deliberately removed buffering now expect immediate stops.
    legacy=root/'tests/better_kbm/test_sprint_turn.cpp'
    if legacy.exists():
        original=legacy.read_bytes(); text=changes.get(legacy,(None,original))[1].decode()
        text=text.replace('assert(gap.x<0&&gap.y==-127);','assert(gap.x==0&&gap.y==0&&!state.active);')
        updated=text.encode()
        if updated != original: changes[legacy]=(original,updated)
    return {path:pair for path,pair in changes.items() if pair[0]!=pair[1]}


def apply(root):
    changes=plan(root);written=[]
    try:
        for path,(_,data) in changes.items():
            previous.atomic_write(path,data);written.append(path)
    except BaseException:
        for path in reversed(written): previous.atomic_write(path,changes[path][0])
        raise
    return changes


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    args=parser.parse_args()
    try: changes=apply(args.root)
    except (OSError,ValueError,KeyError) as error: parser.exit(1,str(error)+'\n')
    print(f'Movement options ready: {len(changes)} files updated; default 800 degrees/s; release buffering removed.')


if __name__=='__main__':main()
