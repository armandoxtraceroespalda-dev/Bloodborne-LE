#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Remove only recorded, unmodified obsolete native source files."""
from pathlib import Path
import hashlib,json,sys
root=Path(sys.argv[1])
manifest=json.loads((root/'better-kbm-manifest.json').read_text())
pending=[]
for name,expected in manifest.get('retired_files',{}).items():
    path=root/name
    if not path.exists():continue
    data=path.read_bytes()
    if b'\0' not in data:data=data.replace(b'\r\n',b'\n')
    if hashlib.sha256(data).hexdigest()!=expected:
        raise SystemExit('Keep your modified source before removing obsolete file: '+str(path))
    pending.append(path)
for path in pending:path.unlink()
if pending:print('Removed obsolete native sources:',len(pending))
