# Appearance direct sliders and preview Y direction (integration 46)

## Behavior

- Reverse the native preview drag Y direction requested after integration 45.
  Horizontal dragging, sensitivity, proportional two-axis input, wheel zoom
  and middle-click reset retain their existing paths.
- Click an appearance slider track to set its value from pointer position.
  Hold LMB and drag to set values directly, with native limits and rounding.
- Capture stays with the clicked row while the cursor crosses another row or
  passes either endpoint. Release, capture/focus cancellation or a fresh press
  ends the previous drag. Arrow clicks still change exactly one unit.

## Implementation

Use the native Slider Bar wrapper at +0xa70 and arrow wrappers at +0xb30/+0xb90.
Bar vertical bounds limit the interactive track; the stable gap between native
arrow clips defines its full horizontal range independent of animated fill.
All bounds use the existing stage transform, including scaled game windows.
The existing native setter updates label, bar and appearance callback. Repeated
stationary positions do not emit duplicate value changes. Labels outside the
track remain row selection targets rather than changing the value.

Both nested ChrMake_MultiSlider rows and standalone native Slider dialogs use
the direct path. A captured multi-row slider is revalidated against its owner's
row vector before updating. Guest callbacks own capture state, while the input
thread cancels it through an atomic epoch. Pointer held state is read under the
existing mouse mutex. The old held-direction slider adapter is disabled only
when installation of the native slider hooks succeeded; fallback remains for
unsupported signatures. Gamepad slider behavior remains native.

## Validation

- Native Bar wrapper/name/constructor path verified against local plaintext
  Bloodborne 1.09 ELF; no game bytes included.
- Actual pointer implementation tests flat, embedded and standalone tracks:
  click midpoint, drag to endpoints and outside the track, retain the original
  row across row boundaries, stop on release/cancel, and exact arrow increments.
- Input bridge tests verify no duplicate held direction when native slider
  handling is enabled, and the existing DS3/editor input regressions pass.
- Preview rotation tests verify the reversed Y sign and unchanged X sign.
- Windows cross-compiler syntax check and full source manifest verification pass.
- Real-game mouse feel and alignment still require player testing.

Apply this delta after integration 45. Close the launcher and run
`bash tools/build-better-kbm.sh`. Keep BetterKBM-engine-update36 selected;
the native session log must report integration 46.
