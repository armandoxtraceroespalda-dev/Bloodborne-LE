# Integration 36 — upstream 0.9.37

Lantern Engine by Awaken (https://github.com/Awakenchan/LanternEngine).
This is a modified BetterKBM edition, not an official Lantern Engine release.

Upstream: d901fbd2ea4bea114b37c26a2a9278d1e97f7b46, parent
08173e6cbfcfd2191594ca28ad8373c7e9ccc895. User baseline: Integration 33
plus Launcher 35. This delivery contains changed files only.

## Integrated

- Upstream built-in game patches and checks, including slope/running-jump timing
  and cloth behavior at variable frame rates; renderer occlusion model updates.
- Reworked feature installation, native state, appearance editor/preview, mouse
  event handling, hover geometry and menu pointer behavior.
- Runtime, loader, script, shader and build changes present in that upstream commit.
- In-play save backups: ten-minute checks, thirty-second quiet period, transaction
  exclusion, copy validation and an independent six-copy retention budget.
- User-selected launcher icon propagation to existing shortcuts, while the game
  retains its separately extracted icon. The compiled app.ico is not replaced.
- GPL-3.0-or-later notices and original-author attribution in Settings and README.

## Preserved custom behavior

The custom native option-menu builder and asset serializer remain, rather than
replacing the Controls/Environment/Graphics layout with upstream's separate PC
pages. Their existing serialization tests still run. Upstream's alternate
OptionKeys module is consequently not compiled or shipped here. OptionRows is
kept as the upstream reusable module and tested independently.

The new input/pointer/camera implementation retains DS3 chords and defaults,
T for blood bullets, item cycling both ways, modifier-wheel weapon switching,
ordered opposing movement, adjustable diagonal sprint turning, independent pad
bindings, keyboard fallback in pad mode and pad suppression in keyboard mode.
Inventory WASD/Space filtering only changes UI handling, not gameplay movement.
The mouse gain remains 0.1 degrees per count at sensitivity 5; physical camera
stick input wins over queued mouse counts. Regional text/voice overrides and
external settings synchronization are retained. Launcher 34/35's hidden Cheats
navigation, bounded typography and responsive upscaler controls are preserved.

The custom Bloodborne GUI, disabled updater policy, extracted local GUI assets,
compiled launcher icon, user configurations and existing game files are retained.

## Build and validation

Run `bash tools/build-better-kbm.sh` in MSYS2 CLANG64. The default output is
`../BetterKBM-engine-update36`; select it in the launcher. The log must identify
`BetterKBM native engine integration 36 | upstream d901fbd2...`.
Old obsolete source files are pruned only when their recorded hashes match.
Engine deployment still refuses to overwrite locally modified target sources.

Validated here: Windows WPF cross-publish; production input, pointer, sprint,
wheel, mouse-camera assembly, shared-camera settings, output policy, localized
text/voices, native menu serialization, controller queue and quit tests; upstream
patch/asset/run-settings suites, hook/row/rebirth/occlusion tests; portable backup
retention tests; Windows native shim syntax checks with SDL3 headers.
A full Windows Vulkan link and actual gameplay, rendering, FPS and memory usage
remain to be tested on the user's PC. No remote branch, commit or PR was modified.
