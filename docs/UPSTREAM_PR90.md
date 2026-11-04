# PR #90: stable lamp fog

This fork backports [Awakenchan/LanternEngine#90](https://github.com/Awakenchan/LanternEngine/pull/90),
by **rhkim1292**, from `rhkim1292/LanternEngine` commit
`d6f4ea38cdfef90eb1bf0cd401a687764eb5792f` (PR base
`4ab0b715969059700bad6454b0a9df127c78ba27`). The PR was open when integrated.
The release baseline remains upstream 0.9.39 beta29,
`e6b943dd969d99561545315f3d801f10e4a6cf8d`; this is a backport, not a full beta30 update.

## Behavior

The game can list the same point light in several screen tiles and omit it when
its lit surfaces leave the screen. Selecting only the first 64 records made lamp
fog disappear as the camera moved. The fix deduplicates the frame's lights,
remembers them in world space, ranks their visible contribution and fades light
and color changes. Nearby stationary lamps are retained; moving torches expire
quickly. Different light reaches distinguish neighboring lamps. A camera jump
clears remembered lights. Bounds remain 4096 gathered records, 1024 remembered
lights and 64 lights sent to the fog pass.

Camera matrices are supplied before both the upscaler and HDR-post composition
paths. Without a valid camera, the implementation uses the current frame's lights
without retaining history. Explicit `BB_UPSCALER=none` disables camera tracking
in upstream; the ordinary launcher Off selection does not require that override.

## Fork adaptation

The correction runs directly with the existing lamp-fog effect. No launcher
option, translation entry or `BB_POINT_FOG_STEADY` environment override is added.
The PR's legacy toggle branch is omitted. The upstream helper and regression
test are preserved, including their tests of the original selection helper.
The header combines the new lamp-fog state with the existing PR #91 tint timing;
neither correction can disable the other. The sunlight correction from update
124 remains unconditional as well.

The upstream Windows offline workflow includes `lamp-fog-lights-test`. Integration
manifest 57 records the exact PR provenance and updated hashes (2525 entries).

## Validation

- The upstream C++20 lamp-fog regression test compiles with GCC 13, `-O2`,
  `-Wall -Wextra -Werror`, assertions enabled, and passes.
- The same test passes with AddressSanitizer and UndefinedBehaviorSanitizer.
  LeakSanitizer is disabled because this execution environment prevents its
  process inspection; this is not a claim that a leak scan passed.
- Tests cover deduplication, arrival-order independence, ranking and ties,
  invalid records, bounds, camera motion, persistence and expiry, neighbor
  separation, moving lights, frame-rate-independent fades, light-limit
  transitions, camera jumps and missing-camera fallback.
- Source verification passes for integration 57 and all 2525 engine entries.
- Integration checks confirm both camera handoffs, unconditional correction,
  unchanged PR #91 shader/test and no new launcher switch. `git diff --check`
  is clean.

This update changes engine code, not launcher behavior. A complete Windows
engine link and gameplay test are not available here. Build locally using
`bash tools/build-better-kbm.sh` with the game and launcher closed, then check
lamp-heavy areas such as the first messenger lamp in the Forbidden Woods.

As noted by the PR author, retaining a lamp can leave a glow after the game turns
it off, or reveal nearby lamp fog through a wall. These are limitations of the
upstream fix, not newly added launcher settings.
