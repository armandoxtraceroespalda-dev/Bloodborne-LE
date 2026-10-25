#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Verify the native 1.09 preview input gates in a player's plaintext ELF. No game data is shipped."""
from pathlib import Path
import struct,sys

raw=Path(sys.argv[1]).read_bytes()
if raw[:7]!=b'\x7fELF\x02\x01\x01':raise SystemExit('Use the plaintext out/eboot.elf prepared by the engine.')
phoff=struct.unpack_from('<Q',raw,0x20)[0];count=struct.unpack_from('<H',raw,0x38)[0]
segments=[struct.unpack_from('<IIQQQQQQ',raw,phoff+56*i) for i in range(count)]
def read(va,n):
    for kind,flags,off,start,physical,size,memory,alignment in segments:
        if kind==1 and start<=va and va+n<=start+size:return raw[off+va-start:off+va-start+n]
    raise ValueError('Unmapped ELF address '+hex(va))
def target(call):
    assert read(call,1)==b'\xe8'
    return call+5+struct.unpack('<i',read(call+1,4))[0]
for call,destination in [(0x1b07b15,0x1ae0d30),(0x1b07b8c,0x1ae0e10),
                          (0x1b07c0b,0x1ae0ef0),(0x1b07c8a,0x1ae0fd0)]:
    assert target(call)==destination
for at,value in [(0x1ae4f06,.2),(0x1ae4de6,-.2),(0x1ae4cc6,.2),(0x1ae4ba6,-.2)]:
    code=read(at,8)
    assert code[:4] in (bytes.fromhex('c5f82e05'),bytes.fromhex('c5fa100d'))
    constant=at+8+struct.unpack_from('<i',code,4)[0]
    assert abs(struct.unpack('<f',read(constant,4))[0]-value)<1e-6
for at,axis in [(0x1ae4efc,0x48),(0x1ae4ddc,0x48),(0x1ae4cbc,0x49),(0x1ae4b9c,0x49)]:
    assert read(at,5)==b'\xbe'+struct.pack('<I',axis)
print('PASS: native ChrMake_BG update reads analog actions 0x48/0x49 through strict +/-0.2 gates; small one-pixel stick deflections are insufficient.')
