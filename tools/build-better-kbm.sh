#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Build the vendored engine in place; its manifest supplies the integration identity.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
engine_target="$root/engine"
[[ ${MSYSTEM:-} == CLANG64 ]] || { echo 'Open MSYS2 CLANG64 and run this script there.' >&2; exit 1; }
for executable in cmake ninja clang python3 cygpath curl; do
    command -v "$executable" >/dev/null || { printf 'Missing dependency: %s\n' "$executable" >&2; exit 1; }
done
for header in expat.h nlohmann/json.hpp; do
    [[ -f "${MINGW_PREFIX:-/clang64}/include/$header" ]] || {
        echo 'Run: pacman -S --needed mingw-w64-clang-x86_64-expat mingw-w64-clang-x86_64-nlohmann-json' >&2; exit 1;
    }
done
python3 "$root/tools/apply-cinematic-voice.py" "$engine_target"
python3 "$root/tools/verify-engine-sources.py" "$engine_target" "$engine_target/better-kbm-manifest.json"
python3 - "$root" "$engine_target" <<'PY'
from pathlib import Path
import json,os,shutil,subprocess,sys,time
root,target=map(Path,sys.argv[1:])
def local_path(value):
    path=Path(os.path.expandvars(value))
    if path.exists():return path
    result=subprocess.run(['cygpath','-u',str(path)],capture_output=True,text=True)
    return Path(result.stdout.strip()) if result.returncode==0 else path
def normalize(path):
    return path/'engine' if (path/'engine/scripts/run_windows.py').is_file() else path
previous=None
if os.environ.get('BB_PREVIOUS_ENGINE'):
    previous=normalize(local_path(os.environ['BB_PREVIOUS_ENGINE']))
elif os.environ.get('APPDATA'):
    for folder in ['Lantern Engine','bbport-launcher']:
        config=local_path(os.environ['APPDATA'])/folder/'settings.json'
        try:
            selected=json.loads(config.read_text(encoding='utf-8-sig')).get('port_dir','')
            candidate=normalize(local_path(selected)) if selected else None
            if candidate and candidate.is_dir() and candidate.resolve()!=target.resolve():
                previous=candidate;break
        except (OSError,ValueError):pass
if previous is None:
    # Detect old build targets without relying on an integration-specific folder name.
    candidates=[p for p in root.parent.glob('BetterKBM-engine-*') if p.is_dir() and p.resolve()!=target.resolve()]
    if candidates:previous=max(candidates,key=lambda p:p.stat().st_mtime)
if previous is not None and previous.is_dir() and previous.resolve()!=target.resolve():
    for name in ['lantern.ini','bbport.ini','mods.json','patches.json','user','mods','network','engine-assets','fsr4_shaders','fsr4_411','fsr4vk','shader-catalog']:
        source=previous/name
        if not source.exists():continue
        for path in [source] if source.is_file() else (p for p in source.rglob('*') if p.is_file()):
            relative=Path(name) if source.is_file() else Path(name)/path.relative_to(source)
            if 'cache' in relative.parts or '__pycache__' in relative.parts:continue
            destination=target/relative
            if not destination.exists():destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,destination)
if not (target/'lantern.ini').exists():
    if (target/'bbport.ini').is_file():shutil.copy2(target/'bbport.ini',target/'lantern.ini')
    else:(target/'lantern.ini').write_text('# Local engine settings; defaults apply until changed in the launcher.\n')
# Future archive timestamps can force endless CMake/Ninja regeneration.
manifest=json.loads((target/'better-kbm-manifest.json').read_text())
now=time.time()
for name in manifest['files']:
    path=target/name
    if path.is_file() and path.stat().st_mtime>now:os.utime(path,(now,now))
PY
compat_windows="$(python3 "$root/tools/canonical-build-path.py" "$(cygpath -m "$engine_target/gpu/win_compat.h")")"
export CFLAGS="${CFLAGS:-} -Wno-error=unused-but-set-global"
export CXXFLAGS="-include \"$compat_windows\" ${BB_EXTRA_CXXFLAGS:-}"
(
    cd -- "$engine_target"
    cmake --fresh -S gpu -B out/gpu -G Ninja
    bash build.sh
    bash tools/fetch_dlss.sh
    if grep -Eq '^upscaler[[:space:]]*=[[:space:]]*fsr4[[:space:]]*$' lantern.ini; then bash tools/fetch_fsr4_assets.sh; fi
    if grep -Eq '^frame_gen[[:space:]]*=[[:space:]]*dlss[[:space:]]*$' lantern.ini; then bash tools/fetch_streamline.sh; fi
)
for program in bb-probe.exe bb-engine-assets.exe bb-start.exe bb-shader-lab.exe; do
    [[ -s "$engine_target/out/$program" ]] || { printf 'Missing built program: %s\n' "$program" >&2; exit 1; }
done
python3 "$root/tools/deploy-runtime-dlls.py" "$engine_target/out" --dll-dir /clang64/bin
python3 - "$engine_target" <<'PY'
from pathlib import Path
import hashlib,json,sys,datetime
root=Path(sys.argv[1]);out=root/'out'
manifest=json.loads((root/'better-kbm-manifest.json').read_text())
stamp={'integration':manifest.get('integration','better-kbm-native-source'),'version':manifest['version'],
       'upstream':manifest['launcher_source_revision'],'upstream_release':manifest.get('upstream_release',''),
       'built_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
       'executables':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [out/'bb-probe.exe',out/'bb-start.exe',out/'bb-shader-lab.exe',out/'bb-engine-assets.exe']}}
(out/'better-kbm-build.json').write_text(json.dumps(stamp),encoding='utf-8')
print('Verified native build:',stamp['upstream_release'],stamp['upstream'])
PY
bash "$root/tools/build-launcher-only.sh"
printf '\nLauncher: %s\nEngine folder: %s\n' "$(cygpath -w "$root/Bloodborne Launcher.exe")" "$(cygpath -w "$engine_target")"
echo 'The launcher automatically detects the local engine folder after a successful build.'
