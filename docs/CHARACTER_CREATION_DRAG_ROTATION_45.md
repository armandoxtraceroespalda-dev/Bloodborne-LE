# Proportional appearance preview drag (integration 45)

## Cause

The former adapter converted mouse displacement to 8-bit right-stick axes,
forced any nonzero component above the native direction threshold, replaced
both held axes at 8 ms intervals and repeated the last vector for 50 ms.
The native preview reads directional threshold booleans rather than analog
rotation magnitude. A new Y-only sample could replace an earlier X sample
before the preview sampled it, and tiny/large inputs used the same native
rotation increment once above the threshold.

## Change

Drag displacement now accumulates both axes until the native preview frame
consumes them once, without a minimum component, byte quantization, timed hold
or artificial right-stick output. Mouse sensitivity still supplies the gain.
A mutex keeps each two-axis sample and its consumption consistent across input
and guest threads. Reset/release/focus/capture paths clear pending drag motion.

The existing version-checked preview update hook from integration 44 also
calls the original model rotation setter (1.09 VA 0x1db2780) with two System V
float parameters (XMM0/XMM1). It uses native per-mode horizontal/vertical rates
from VA 0x47349e0 and applies the same yaw/pitch signs as the original stick.
Native yaw wrapping, pitch limits and model transforms remain in that setter.
All four model views receive the displacement. If the rotation setter signature
differs, the old stick adapter remains available without disabling native zoom.
No additional hook is installed over the existing preview update hook.

Gamepad input, gameplay camera, click-to-select, slider dragging, arrow clicks,
wheel zoom and middle-click reset retain their existing paths.

## Validation

- Native function signature, float operations and all 16 original rotation
  call sites checked against the player's locally prepared plaintext 1.09 ELF.
- Actual displacement accumulator tested at 30..240 Hz, including alternating
  X/Y-only samples, tiny/fractional input, diagonals, reversal and reset.
- Actual pointer/native bridge tested with a mock two-float native setter for
  four-view updates, signs, rates and invalid/inactive state.
- Actual input bridge tested for fractional X/Y conservation, no duplicate
  stick output, stop/release behavior and previous DS3/editor controls.
- Windows cross-compiler syntax check passes; full native source manifest
  verification passes. Real Windows gameplay remains for player testing.

Apply this delta after integration 44, close the launcher, then run
`bash tools/build-better-kbm.sh`. Keep BetterKBM-engine-update36 selected;
the native session log must report integration 45 and:
`appearance preview: proportional two-axis drag; no stick dead zone or held input`
