# Editor drag delivery — integration 38

Lantern Engine by Awaken (https://github.com/Awakenchan/LanternEngine).
Modified BetterKBM edition based on upstream d901fbd2. Applies after Editor37.

## Investigation

Integration37 fixed editor routing but still consumed mouse motion and slider
pulses on a single pad read. The bridge can be read repeatedly before the native
menu sees its next frame. Previous tests inspected one immediate sample, so they
could not detect this loss. A new regression fails against the previous bridge.

Inspection of the player's plaintext 1.09 executable confirms that the native
ChrMake_BG update at 0x1b07870 queries analog actions 0x48/0x49 through helpers
with strict +/-0.2 gates. One pixel at the old sensitivity-5 gain produced
12/127, below the gate. The opt-in verifier in engine/tools/re/editor_input_facts.py
checks these calls, action IDs and constants against a locally prepared ELF;
no game bytes or assets are included in this delivery.

## Change

EditorDrag::Rotation accumulates motion in eight-millisecond windows, preserves
its axes across intervening reads for at most fifty milliseconds, and raises
nonzero components above the native gate. Releasing the mouse, losing focus,
entering a capture/text field or leaving the editor resets them. The normal
raw mouse camera and physical gamepad axes use their existing paths.

EditorDrag::Steps preserves each slider press for forty-five milliseconds and
its release for forty milliseconds. Its pending count is bounded and allocation
free. Releasing or closing the editor cancels pending input immediately.
Rotation and slider output have four bounded diagnostic lines each, to identify
whether native editor activation and pad delivery occur in a real session.

## Verification

Production input regressions now check one-pixel rotation and intervening reads,
not just a large immediate delta. The standalone timing matrix simulates 1 kHz
reads, 30/60/90/120/144/240 FPS and 34 phase offsets. The lifetime fixture uses
production engine_state.cpp and change_appearance.cpp to verify observation,
retention and single release of ChrMakeCommandList; only guest memory/calls are
synthetic. Existing controls, pointer, sprint, camera, native assets, controller,
quit, language and settings regressions pass. Windows input-module syntax passes.
Actual Windows gameplay has not been executed in this environment.

Rebuild with bash tools/build-better-kbm.sh; the existing default target remains
BetterKBM-engine-update36. The native startup log must identify integration 38.
No remote branch or commit is changed.
