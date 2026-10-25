#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Integration 47: build the integrated 0.9.37 source in a separate target.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# Keep the previous two-argument convention; the old base checkout is no longer needed.
engine_target="${2:-${BB_ENGINE_TARGET:-$root/../BetterKBM-engine-update36}}"
previous="${BB_PREVIOUS_ENGINE:-$root/../BetterKBM-engine-beta9}"
[[ -d "$previous" ]] || previous="$root/../BetterKBM-engine-expanded"
[[ ${MSYSTEM:-} == CLANG64 ]] || { echo 'Open MSYS2 CLANG64 and run this script there.' >&2; exit 1; }
for executable in cmake ninja clang python3 cygpath curl; do
    command -v "$executable" >/dev/null || { printf 'Missing dependency: %s\n' "$executable" >&2; exit 1; }
done
for header in expat.h nlohmann/json.hpp; do
    [[ -f "${MINGW_PREFIX:-/clang64}/include/$header" ]] || {
        echo 'Run: pacman -S --needed mingw-w64-clang-x86_64-expat mingw-w64-clang-x86_64-nlohmann-json' >&2; exit 1;
    }
done
python3 "$root/tools/prune-obsolete-engine.py" "$root/engine"
python3 "$root/tools/verify-engine-sources.py" "$root/engine" "$root/engine/better-kbm-manifest.json"
python3 - "$root/engine" "$engine_target" "$previous" <<'PY'
from pathlib import Path
import hashlib,json,shutil,sys,time,os,subprocess
source,target,previous=map(Path,sys.argv[1:]);manifest=json.loads((source/'better-kbm-manifest.json').read_text())
def digest(p):
    data=p.read_bytes()
    if b'\0' not in data:data=data.replace(b'\r\n',b'\n')
    return hashlib.sha256(data).hexdigest()
record=target/'better-kbm-source-manifest.json'
old=json.loads(record.read_text()) if record.exists() else None
if target.exists() and any(target.iterdir()) and not old:
    raise SystemExit('Choose a new empty target directory. The previous engine has not been modified.')
for name,item in manifest['files'].items():
    p=target/name
    if p.exists() and old and digest(p) not in {item['after'],old.get('files',{}).get(name,{}).get('after')}:
        raise SystemExit('Local source changes in '+str(p)+'. Keep this folder and choose a new target.')
if old:
    obsolete=set(old.get('files',{}))-set(manifest['files'])
    for name in obsolete:
        p=target/name
        if p.exists() and digest(p)!=old['files'][name]['after']:
            raise SystemExit('Local source changes in obsolete file '+str(p)+'. Choose a new target.')
    for name in obsolete:
        p=target/name
        if p.is_file():p.unlink()
for name in manifest['files']:
    p=target/name;p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source/name,p)
record.write_text(json.dumps(manifest,indent=2)+'\n')
# Prefer the engine actually selected by the launcher; an explicit override wins.
if not os.environ.get('BB_PREVIOUS_ENGINE') and os.environ.get('APPDATA'):
    def local_path(value):
        p=Path(value)
        if p.exists():return p
        result=subprocess.run(['cygpath','-u',value],capture_output=True,text=True)
        return Path(result.stdout.strip()) if result.returncode==0 else p
    try:
        appdata=local_path(os.environ['APPDATA'])
        for folder in ['Lantern Engine','bbport-launcher']:
            config=appdata/folder/'settings.json'
            if not config.exists():continue
            selected=json.loads(config.read_text(encoding='utf-8-sig')).get('port_dir','')
            if not selected:continue
            active=local_path(selected)
            if (active/'engine/scripts/run_windows.py').exists():active=active/'engine'
            if active.is_dir() and active.resolve()!=target.resolve():previous=active;break
    except (OSError,ValueError):pass
if previous.resolve()!=target.resolve() and previous.is_dir():
    for name in ['lantern.ini','bbport.ini','mods.json','patches.json','user','mods','network','engine-assets','fsr4_shaders','fsr4_411','fsr4vk','shader-catalog']:
        src=previous/name
        if not src.exists():continue
        for p in [src] if src.is_file() else (p for p in src.rglob('*') if p.is_file()):
            relative=Path(name) if src.is_file() else Path(name)/p.relative_to(src)
            if 'cache' in relative.parts or '__pycache__' in relative.parts:continue
            dest=target/relative
            if not dest.exists():dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
if not (target/'lantern.ini').exists() and (target/'bbport.ini').exists():shutil.copy2(target/'bbport.ini',target/'lantern.ini')
now=time.time()
for name in manifest['files']:
    p=target/name
    if p.stat().st_mtime>now:p.touch()
PY
engine_target="$(cd -- "$engine_target" && pwd)"
python3 "$root/tools/verify-engine-sources.py" "$engine_target" "$root/engine/better-kbm-manifest.json"
compat_windows="$(cygpath -m "$engine_target/gpu/win_compat.h")"
export CFLAGS="${CFLAGS:-} -Wno-error=unused-but-set-global"
export CXXFLAGS="-include \"$compat_windows\" ${BB_EXTRA_CXXFLAGS:-}"
(
    cd -- "$engine_target"
    cmake --fresh -S gpu -B out/gpu -G Ninja
    bash build.sh
    bash tools/fetch_dlss.sh
    if grep -Eq '^upscaler[[:space:]]*=[[:space:]]*fsr4[[:space:]]*$' lantern.ini 2>/dev/null; then bash tools/fetch_fsr4_assets.sh; fi
    if grep -Eq '^frame_gen[[:space:]]*=[[:space:]]*dlss[[:space:]]*$' lantern.ini 2>/dev/null; then bash tools/fetch_streamline.sh; fi
)
for program in bb-probe.exe bb-engine-assets.exe bb-start.exe bb-shader-lab.exe; do
    [[ -s "$engine_target/out/$program" ]] || { printf 'Missing built program: %s\n' "$program" >&2; exit 1; }
done
python3 "$root/tools/deploy-runtime-dlls.py" "$engine_target/out" --dll-dir /clang64/bin
python3 - "$engine_target" <<'PY'
from pathlib import Path
import hashlib,json,sys,datetime
root=Path(sys.argv[1]);out=root/'out'
stamp={'integration':'better-kbm-native-source','version':47,'upstream':'d901fbd2ea4bea114b37c26a2a9278d1e97f7b46','upstream_release':'0.9.37 (d901fbd2)','built_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
       'executables':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [out/'bb-probe.exe',out/'bb-start.exe',out/'bb-shader-lab.exe',out/'bb-engine-assets.exe']}}
(out/'better-kbm-build.json').write_text(json.dumps(stamp),encoding='utf-8')
print('Verified native build:',stamp['upstream_release'],stamp['upstream'])
PY
bash "$root/tools/build-launcher-only.sh"
printf '\nLauncher: %s\nEngine folder: %s\n' "$(cygpath -w "$root/LauncherApp/Bloodborne Launcher.exe")" "$(cygpath -w "$engine_target")"
echo 'IMPORTANT: select the NEW BetterKBM-engine-update36 folder in the launcher before testing.'
echo 'The session log must say: BetterKBM native engine integration 47 | upstream d901fbd2...'
