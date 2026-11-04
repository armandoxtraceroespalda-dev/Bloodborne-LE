# Upstream beta 24.1 integration

- Upstream: Awakenchan/LanternEngine, `60783baf1fb932f367c5627fb043b711f6a917e1`.
- Previous upstream: beta 22, `a2e858c4d02f3eeba083c3449cdc06722baeabcd`.
- Local source integration: 52 (source update package 112).

The native Direct2D launcher remains the only launcher. Upstream WPF views are not installed over it. Shared save services and their validations are source-linked into the native frontend.

## Launcher

- Dedicated Saves page after Game: player profiles, playtime, backups, safety copies, restore, import and saves location. Existing manual backup actions remain available.
- More > Save editor: character and profile selection, attributes and blood echoes/insight, inventory quantities, adding goods, weapons and armor, base-game/DLC filters, boss state and lamp editing, and editor-backup restoration. Item, boss and lamp names are read locally from the selected game text language, including American Spanish.
- The editor stages changes in memory. Saving requires the game to be closed, verifies that the source save has not changed, validates the result, creates a backup and updates the character summary when the level changes. Leaving with pending edits asks before discarding them.
- Online: server or Direct P2P connections, host address/port, guest address, compatible invite codes, connection-status visibility, party-portrait ping, party notices and warp requests. Host/guest fields follow the selected role.
- More > Engine: all six new native-renderer/compiler switches.
- Settings: opt-in tested settings with a saved copy of previous values and a restore action. No launcher update/version-change controls are restored.
- Save safety copies before and after a game session, recovery prompts for a missing save location, and periodic backups during play when backups are enabled.
- New screens follow the existing text-language selection, layout, sounds, transitions and page-resource lifetime.

## Engine

Integrates upstream beta 23 through beta 24.1, including native rendering/shader work, file and video subsystem changes, frame-rate handling and co-op improvements (party state, shared progress, travel and spectating). Local controls, inventory movement, appearance-editor mouse behavior and independent text/voice/cinematic language selection are retained.

Sixty-six removed or renamed source modules are retired by `tools/prepare-upstream-update.py` before compilation. Matching old sources are archived under `engine/.source-migration/112`; modified old sources cause a review error instead of being deleted. Saves, configuration, extracted GUI assets and runtime DLLs are not part of source retirement.

## Validation

The native launcher builds in Release with zero warnings/errors; native contract tests, original save-editor binary-format/write/restore checks, local input/movement/camera/language tests, cinematic selection tests, native GPU-tool runtime-link checks, source migration and source integrity checks pass. Upstream C++ frame-rate, party-death, shared-progress and kept-travel tests pass.

The Windows/Vulkan engine build, actual game rendering and multiplayer sessions must still be tested on a Windows gaming PC. No FPS or visual parity claim is made from these source-level checks.
