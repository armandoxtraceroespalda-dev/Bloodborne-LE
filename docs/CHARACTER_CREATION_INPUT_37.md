# Character-creation input correction — integration 37

Lantern Engine by Awaken (https://github.com/Awakenchan/LanternEngine).
This is a modified BetterKBM edition, based on upstream d901fbd2.
This delta applies after Integration36-Cambios; it contains no upstream update.

## Reproduced incompatibilities

The integrated input bridge used list-focus/menu-flow state to own the mouse,
without considering the appearance editor's separate lifetime. List focus can
lapse while its preview or child widgets are active, allowing gameplay mouse
handling to resume. The previous in-world WASD policy also applied to the mirror.
The original editor mouse-binding helper used gameplay assignments: the DS3
profile turns RMB into L2, so RMB failed to send editor Back. Modifier-wheel
bindings could likewise queue a weapon switch when editor lists lost focus.

## Correction

- The native editor lifetime keeps the pointer and menu routing active across
  list-focus gaps, both at character creation and the Dream mirror.
- WASD is editor navigation there; world movement/list suppression remains
  specific to inventory-style menus outside the editor.
- LMB selects or drags, RMB sends native Back, and wheel selects editor tabs,
  independent of DS3 combat/weapon/item bindings. Dragging the preview supplies
  right-stick rotation. The gameplay mouse camera stays disabled in the editor.
- Releasing a slider or leaving/capturing the editor clears queued slider steps
  and releases the pointer's row hold. Normal gameplay bindings resume afterward.
- Editor open/close transitions are logged, without per-frame log allocations.

## Validation and deployment

New regression assertions fail against integration36's original input bridge.
With this correction, production input/pointer regressions cover missing list
focus, RMB versus L2, modifier-wheel event dispatch, WASD versus world movement,
preview rotation, slider direction pulses, release and editor closure. Existing
DS3/inventory/sprint/controller, native asset and localized voice/text suites pass.
Windows cross-compiler syntax checks pass for the three changed runtime modules.
Actual Windows gameplay/UI behavior cannot be verified in this environment.

Rebuild with `bash tools/build-better-kbm.sh`. The existing default target stays
`../BetterKBM-engine-update36` to reuse its build output and settings. Select that
folder; the log must say `BetterKBM native engine integration 37` and upstream
`d901fbd2`. Rebuilding only the WPF launcher cannot deploy this native correction.
