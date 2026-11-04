#!/usr/bin/env python3
"""Install the official, unmodified Vulkan FSR SDK runtime next to the engine."""
from pathlib import Path
import hashlib
import json
import os
import sys
import urllib.request

COMMIT = '7539ae8b6568ad29ed78a753404351daf3b5cca7'
BASE = f'https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/{COMMIT}/'
FILES = {
    'amd_fidelityfx_loader_vk.dll': ('Kits/FidelityFX/signedbin/amd_fidelityfx_loader_vk.dll', 39184, '38a56a01fb3565c0757d9f5c1e4e69de4b65e5f2e5769b323b200dc1f142bab9'),
    'amd_fidelityfx_upscaler_vk.dll': ('Kits/FidelityFX/signedbin/amd_fidelityfx_upscaler_vk.dll', 36135696, '4a7e895af6e0b615d40a84d59eb8c2b1054ad91cdd0c739567940270d51bdab7'),
    'LICENSE-AMD-FSR-SDK.md': ('Kits/FidelityFX/docs/license.md', 65770, '32357259b965d6a8001e9e508ba60eda6034027c5923a7d226ffbaff813c2bfb'),
}


def fetch(engine):
    if os.environ.get('BB_FSR_SDK_DIR'):
        print('FSR SDK: keeping the explicitly configured BB_FSR_SDK_DIR.')
        return
    folder = Path(engine) / 'fsr_sdk'
    folder.mkdir(parents=True, exist_ok=True)
    records = {}
    for name, (relative, size, sha) in FILES.items():
        target = folder / name
        if target.is_file():
            current = hashlib.sha256(target.read_bytes()).hexdigest()
            if current != sha:
                # A locally supplied SDK is player data. Never replace it silently.
                print(f'FSR SDK: keeping locally supplied {name}.')
            records[name] = current
            continue
        temporary = target.with_suffix(target.suffix + '.download')
        digest = hashlib.sha256()
        received = 0
        try:
            request = urllib.request.Request(BASE + relative, headers={'User-Agent': 'Bloodborne-LE-build'})
            with urllib.request.urlopen(request, timeout=60) as response, temporary.open('wb') as output:
                while block := response.read(128 * 1024):
                    received += len(block)
                    if received > size:
                        raise ValueError(f'{name}: unexpected download size')
                    digest.update(block)
                    output.write(block)
            if received != size or digest.hexdigest() != sha:
                raise ValueError(f'{name}: official runtime size or SHA-256 mismatch')
            os.replace(temporary, target)
            records[name] = sha
        finally:
            temporary.unlink(missing_ok=True)
    (folder / 'SOURCE.txt').write_text(
        'AMD FidelityFX SDK v2.4.0, Vulkan signed binaries, unmodified.\n'
        f'https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/tree/{COMMIT}\n'
        + json.dumps(records, indent=2) + '\n', encoding='utf-8')
    print('FSR SDK: runtime and redistribution license ready in engine/fsr_sdk.')


if __name__ == '__main__':
    try:
        if len(sys.argv) != 2:
            raise ValueError('Usage: fetch-fsr-sdk.py ENGINE_FOLDER')
        fetch(sys.argv[1])
    except (OSError, ValueError) as error:
        raise SystemExit(f'FSR SDK preparation failed: {error}')
