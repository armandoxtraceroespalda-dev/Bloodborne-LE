# Resolve embedded appearance sliders

Integration 43, applied after Editor42. Upstream remains d901fbd2.

The supplied game session confirms integration 42, successful hook installation and an active Dream mirror appearance editor. It contains drag output but no successful slider row or arrow hits.

## Cause and correction
Editor42 assumed every row in the multi-slider vector was a native Slider dialog. The actual appearance constructor creates an outer row dialog (vtable 0x539b6e0, update 0x1ad5b10) and embeds the Slider at row+0xa70. The strict update check in MultiUnder rejected these real rows before hit testing. Synthetic tests had incorrectly used flat Slider rows only.

SliderFromRow now recognizes both verified layouts. The outer row supplies the visible row bounds; the embedded Slider supplies arrow objects, integer value, range and native SetValue target. Endpoints remain clickable when the game's focus-only arrow drawings are hidden, provided the appearance row itself is visible. No input profile or control binding changes are required. Middle-click preview reset and the 80 ms zoom are unchanged.

## Evidence and validation
The native ELF facts check now follows the actual ChrMake_MultiSlider factory, its derived update, the row factory and row constructor. It verifies the +0xa70 embedded Slider construction and the distinct outer/inner vtable updates through ELF relocations.

The production pointer test now runs with flat and embedded rows. The embedded-row test fails with Editor42 at row selection and passes with Editor43. Tests also cover exact one-unit changes, limits, native change notification, inactive arrow drawings, standalone sliders, inactive focus and non-mouse input. Windows-target compilation syntax and the full 2305-file native manifest pass. Windows gameplay still requires user testing.

A successful real layout lookup emits once:
Input: appearance pointer: embedded Slider resolved at row+0xa70
Unmatched click locations are reported at most four times for troubleshooting.

## Install
Close the game and launcher. Copy ARCHIVOS into the existing clone, replacing the included files. Run bash tools/build-better-kbm.sh and keep BetterKBM-engine-update36 selected. The session must report BetterKBM native engine integration 43.

Only changed source, tests and documentation are included; no settings, DLLs, compiled executables or game assets.
