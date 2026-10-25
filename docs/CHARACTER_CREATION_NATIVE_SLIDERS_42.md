# Native appearance sliders and preview reset

Integration 42, applied after Editor41; upstream remains d901fbd2.

## Cause
The Features/body/face adjustment panels use ChrMake_MultiSlider, a native dialog containing a vector of Slider dialogs. Its row selection does not call the generic list update hooked by Editor41. Extending the generic list hit regions therefore could not enable mouse row selection in these panels.

## Implementation
- Hook the native multi-slider update and only its navigation-reader call site. The shared navigation function remains unchanged.
- Read the actual row vector and row/arrow display wrappers. Pointer selection is applied after the original update snapshots its previous index, allowing the game's own update to change the active child, highlight, and sound.
- Hook native Slider updates too, covering individual appearance sliders in addition to multi-slider panels.
- Use the game's SetValue function for arrow clicks with current value plus/minus exactly one. It owns clamping, numeric/bar display updates, appearance notifications and the standard change sound. Clicks do not simulate held directional input.
- All new pointer behavior requires an active appearance preview, mouse menu input and active native focus. Existing keyboard/gamepad navigation delegates to the original reader.
- Middle mouse sends native R3 to reset the model preview independently of gameplay bindings.
- Keep Editor41's 80 ms wheel zoom with no duration backlog, existing drag/rotation support, DS3 profile, inventory movement and personal settings.

Native hooks and layout signatures are checked before installation. On mismatch the existing pointer remains available and a diagnostic is logged. The install marker is:
Input: appearance pointer: ChrMake_MultiSlider hooked; native row selection and single-unit arrow clicks

## Validation
- Production pointer code tested with synthetic native dialog/row/vector/wrapper objects, without a generic list callback: selection, exact one-unit increments/decrements, limits, native value notification, standalone slider updates, inactive focus, and non-mouse input.
- Production input bridge tests pass, including middle-click R3, existing DS3 chords/wheel behavior, inventory movement and preview dragging.
- Windows-target pointer compilation syntax checked with warnings enabled.
- appearance_slider_facts.py verifies the real plaintext 1.09 ELF: multi-slider update, old/new cursor comparison timing, dedicated navigation call, row root/arrow/value offsets, native notifying setter call, and original preview reset branch.
- Full native source manifest verifies integration 42, 2305 files.

Windows gameplay still requires user testing.

## Build
Close the game and launcher. Copy ARCHIVOS into the existing clone, then run bash tools/build-better-kbm.sh. Keep BetterKBM-engine-update36 selected; the directory name is retained, while the session must report BetterKBM native engine integration 42.

Optional local signature check: python engine/tools/re/appearance_slider_facts.py PATH_TO_ENGINE/out/eboot.elf
No game files, generated assets, configuration files or compiled binaries are included.
