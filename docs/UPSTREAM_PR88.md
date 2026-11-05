# PR #88: 4K menu filtering and HUD art

This selectively backports [Awakenchan/LanternEngine#88](https://github.com/Awakenchan/LanternEngine/pull/88),
by **Ndmjohansen**, commit `0c93a468ff81f5fa2cd1687406f0121a20f5584a`,
based on `e6b943dd969d99561545315f3d801f10e4a6cf8d`. The PR is merged upstream.
Integration 59 keeps the release baseline at 0.9.39 beta29 and preserves the
previous fog and scene-proxy VRAM corrections. It does not import native draws,
cooperative-play updates or other beta30/beta31 changes.

## Changes

- Upload 4K replacements with their full mip chain. Reduction weights color by
  alpha and averages sRGB values in linear light, avoiding dark transparent edges.
- Filter replacement draws linearly between mip levels, with the upstream
  sharpness bias of -0.5. Smooth native menu textures while a 4K pack is loaded,
  preserving additive-glow and depth sampling behavior.
- Keep opaque BC1 RGB replacements opaque, avoiding holes in some icons.
- Generate `hud-4k.bbui` from the existing pack's HUD art. Clean gray gaps around
  bar glows so filtering does not expose a rectangular fringe.
- Draw the HUD from that art when its identity matches, retaining dynamically
  rendered keyboard/gamepad prompts. Clean the native sheet as a fallback for
  older or different packs. Retain at most three HUD prompt variants; retire old
  variants through the GPU scheduler and remove their views.

This changes rendering and packing, not the Real-ESRGAN model or the underlying
art. It does not import the contributor's separate Siax/hd-ui branch. Mip chains
use roughly one third more texture VRAM for the same replacement dimensions;
this is intentional storage, separate from the fixed scene-proxy lifetime leak.
The existing 4K texture budget remains unchanged.

## Launcher and existing packs

No new launcher options are added. Use the existing **Graphics > 4K menus**
controls. Existing packs work immediately with filtering and runtime mipmaps.
To use the new high-resolution HUD, close the game and run the existing build
action again after rebuilding the engine. The packer writes its additional
`hud-4k.bbui` locally. Until then, the runtime uses a cleaned native HUD sheet.
The normal build action may need to upscale the source textures again if its
temporary work folder was removed; no generated assets are distributed here.

Upstream diagnostic overrides are retained in the engine, without adding UI:
`BB_UI_SMOOTH=0` uses original sampling; `BB_UI_LOD_BIAS` accepts -2 to 0,
default -0.5. No changes to resolution selection or scene upscaling are made.

## Validation

- The actual `bb-ui-4k` tool compiles with GCC 13, C++20, `-O2`,
  `-Wall -Wextra -Werror`.
- `python3 engine/tests/test_ui_4k_filtering.py` compiles the production packer,
  HUD decoder/cleaner and extracted production pixel/sampler functions. Tests
  cover alpha-aware reductions, linear-light sRGB reduction, thin mip chains,
  prompt scaling, PNG-to-BBUI packing, HUD fringe cleanup, stale HUD removal,
  truncated containers, independent prompt hashes, masked sheet identity, bounded
  decoding, sampling gates and environment sharpness clamping.
- The same regression suite passes with AddressSanitizer and
  UndefinedBehaviorSanitizer (`--sanitize`). LeakSanitizer is disabled because
  this environment prevents its process inspection.
- Scope checks retain the scene-proxy deletion notification and all previous
  fog code, with no launcher changes. Integration 59 verifies 2530 source entries.
- Source formatting and shell syntax checks pass.

Sampler tests substitute GPU-facing value holders; they do not run Vulkan.
A complete Windows engine link and Bloodborne visual validation remain for the
user's PC. Close game and launcher and run `bash tools/build-better-kbm.sh`.
Then rebuild the 4K pack and inspect HUD bars, item icons, text and both keyboard
and gamepad prompts. No game assets, generated packs or binaries are included.
