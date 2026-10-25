# Appearance preview wheel distance (integration 44)

## Behavior

Fast wheel detents now change preview zoom proportionally. SDL batched deltas
and fractional wheels retain their magnitude. The native preview update consumes
the signed distance once; there is no per-detent trigger queue or timed tail.

## Cause and implementation

The former trigger adapter retained only the sign of integer detents and renewed
one 80 ms hold. Multiple detents within that hold therefore collapsed together.

A version-checked hook on ChrMake_BG update (1.09 VA 0x1b07870) consumes atomic
wheel displacement and calls the native clamped zoom setter (VA 0x1db2990) on
all four model views. The setter uses the System V float ABI (XMM0); the integer
argument bridge must not be used for its float parameter. Per-mode native rates
come from VA 0x47349e0. One wheel detent applies 2.4 native 30 Hz increments,
preserving the previous 80 ms sensitivity without simulating a held trigger.
Gamepad triggers continue through the original native path. Keyboard/mouse
capture, inactive editor and focus changes cancel pending wheel displacement.
If signatures are unavailable, the previous trigger implementation is retained
and the startup log explicitly reports that fallback.

## Validation

- Verified hook, setter, native call sites and rate table against the player's
  locally prepared plaintext 1.09 ELF; no game files are distributed.
- Production wheel accumulator tested at 30..240 Hz with grouped, fractional,
  opposite-direction, non-finite and reset inputs.
- Actual input bridge tested with eight individual SDL events and one batched
  negative event, including suppression of duplicate trigger/gameplay inputs.
- Actual pointer implementation tested with a mock float-ABI native setter:
  eight detents produce eight times the single-detent displacement, one call
  per model view, and invalid model sets are rejected before any partial update.
- Previous pointer, native nested slider, inventory and DS3 input tests pass.
- Windows cross-compiler syntax check passes. Windows gameplay is not available
  in this environment; the player must verify feel and camera limits in game.

Build with tools/build-better-kbm.sh and retain BetterKBM-engine-update36 as the
selected engine directory. The native session identity must report integration 44.
This delta is intended to be applied after integration 43.
