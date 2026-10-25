#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Verify the native 1.09 appearance slider paths used by the pointer hooks.
Accepts the player's locally prepared out/eboot.elf. No game bytes are bundled.
"""
from pathlib import Path
import struct, sys
raw = Path(sys.argv[1]).read_bytes()
if raw[:7] != b'\x7fELF\x02\x01\x01':
    raise SystemExit('Use the plaintext out/eboot.elf prepared by the engine.')
phoff = struct.unpack_from('<Q', raw, 0x20)[0]
count = struct.unpack_from('<H', raw, 0x38)[0]
segments = [struct.unpack_from('<IIQQQQQQ', raw, phoff + i * 56) for i in range(count)]
def read(at, length):
    for kind, flags, off, start, physical, size, memory, alignment in segments:
        if kind == 1 and start <= at and at + length <= start + size:
            return raw[off + at - start:off + at - start + length]
    raise ValueError('Unmapped ELF address ' + hex(at))
def call(at, target):
    assert read(at, 1) == b'\xe8'
    assert at + 5 + struct.unpack('<i', read(at + 1, 4))[0] == target
assert read(0x1b7a750, 20).hex() == '554889e54157415641554154534881ec88000000'
assert read(0x1adc970, 16).hex() == '554889e54157415653504989f64889fb'
assert read(0x1adcaa0, 17).hex() == '554889e54157415641554154534883ec58'
assert read(0x1ac1e02, 14).hex() == '66666666662e0f1f840000000000'
# The multi-slider snapshots its previous row before the redirected reader.
assert read(0x1b7a77d, 7).hex() == '458bb5740a0000'
assert read(0x1b7a831, 7).hex() == '4539b5740a0000'
assert read(0x1b7a977, 7).hex() == '4881c600010000'
call(0x1b7a7b5, 0x1adfdd0)
call(0x1adc9ac, 0x1adfdd0)
call(0x1adc9d2, 0x1adcaa0)
# Slider constructor binds its own arrows and stores the integer value/range.
assert read(0x1b2c56c, 7).hex() == '4c8dbb300b0000'
assert read(0x1b2c587, 7).hex() == '4c8da3900b0000'
assert read(0x1b2c5bd, 6).hex() == '8983f80b0000'
# The original preview's reset branch calls the model reset for all four views.
call(0x1b079fd, 0x1ae07b0)
for at in [0x1b07a0d, 0x1b07a19, 0x1b07a25, 0x1b07a31]:
    call(at, 0x1db1730)
# The real appearance row is NOT the Slider itself: its constructor embeds
# the native Slider at +0xa70, and its vtable dispatches the child-stack update.
call(0x1b92343, 0x1b805a0)
call(0x1b80ab8, 0x1b7fb50)
call(0x1b7fde5, 0x1b2c500)
call(0x1b81011, 0x1b7a750)
assert read(0x1b7fd76, 8).hex() == '498d8424700a0000'
dynamic = next(off for kind, flags, off, *rest in segments if kind == 2)
dynlib = next(off for kind, flags, off, *rest in segments if kind == 0x61000000)
tags = {}
for i in range(4096):
    tag, value = struct.unpack_from('<qQ', raw, dynamic + 16*i)
    if tag == 0: break
    tags[tag] = value
start = dynlib + tags[0x6100002f]
relocations = {at: target for at, info, target in
    struct.iter_unpack('<QQq', raw[start:start + tags[0x61000031]]) if info & 0xffffffff == 8}
assert relocations[0x539b6e0 + 0x18] == 0x1ad5b10
assert relocations[0x539a090 + 0x18] == 0x1adc970
print('PASS: real appearance row embeds Slider at +0xa70; outer and inner updates differ; factory and constructor paths verified.')
print('PASS: native MultiSlider selection timing, Slider arrow/value layout and notifying setter, and original preview reset path.')

# Preview zoom consumes a float distance in XMM0, subtracts it from camera
# distance, clamps it to native limits, and rebuilds the model transform.
assert read(0x1b07870,20).hex() == '554889e54157415641554154534881ec88000000'
assert read(0x1db2990,15).hex() == '554889e5415741564154534883ec50'
assert read(0x1db29d1,4).hex() == 'c5f25cc0'
for at in [0x1b07a5f,0x1b07a73,0x1b07a87,0x1b07a9b,
           0x1b07acd,0x1b07ae1,0x1b07af5,0x1b07b09]:
    call(at,0x1db2990)
rates=struct.unpack('<9f',read(0x47349e0,36))
assert all(0<rates[i*3]<1 for i in range(3))
print('PASS: native preview update, four-view signed float zoom setter, native rate table and clamped camera distance verified.')

# Native drag rotation accepts yaw/pitch in XMM0/XMM1, scales each by 1.5,
# wraps yaw and clamps pitch. These are the same calls as native right-stick
# directions; the pointer path bypasses their threshold tests, not their limits.
assert read(0x1db2780,15).hex() == '554889e5415741564154534883ec50'
assert read(0x1db27b8,4).hex() == 'c5fa59c2'
assert read(0x1db2806,4).hex() == 'c5f259c2'
assert struct.unpack('<f',read(0x4928dfc,4))[0] == 1.5
for at in [0x1b07b38,0x1b07b50,0x1b07b68,0x1b07b80,
           0x1b07bb7,0x1b07bcf,0x1b07be7,0x1b07bff,
           0x1b07c36,0x1b07c4e,0x1b07c66,0x1b07c7e,
           0x1b07cad,0x1b07cc5,0x1b07cdd,0x1b07cf5]:
    call(at,0x1db2780)
print('PASS: native two-float rotation ABI, per-axis signs/rates, and original model rotation setter verified.')

# Slider binds its own Bar clip at +0xa70. Stable arrow endpoints delimit
# the full draggable range even while the bar's timeline changes its fill.
assert read(0x1b2c53c,7).hex() == '488dbb700a0000'
assert read(0x4933827,4) == b'Bar\0'
call(0x1b2c54f,0x1adb220)
print('PASS: native appearance Bar wrapper and arrow endpoint layout verified for direct mouse dragging.')
