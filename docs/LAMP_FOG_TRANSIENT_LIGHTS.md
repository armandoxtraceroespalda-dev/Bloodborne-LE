# Lamp fog retained after fire disappears

Integration 60 applies two follow-up corrections by **rhkim1292** to PR #90:

- [`6e7cb6d9e43b019e6f4849d9c448a7f7339dedb2`](https://github.com/Awakenchan/LanternEngine/commit/6e7cb6d9e43b019e6f4849d9c448a7f7339dedb2):
  stop retaining carried torches and short-lived fire flashes as stationary lamps.
- [`17cc5148f7687c442dc2681f858f7f4f232dccaa`](https://github.com/Awakenchan/LanternEngine/commit/17cc5148f7687c442dc2681f858f7f4f232dccaa):
  stop leaving an old copy when a spawned light jumps to a new position.

## Cause

The earlier PR #90 integration remembers stationary lamps near the camera to
avoid popping when the game temporarily omits them. A torch that stopped moving
for two seconds could be treated as a stationary lamp; when its owner died, its
fog remained. A short fire flash could be retained in the same way. A spawned
light jumping farther than the normal matching tolerance also created a second
entry, leaving the original remembered light behind.

## Behavior

A light that has moved sufficiently far from its initial position remains marked
as carried, even after it stops. Nearby bright spawned flashes listed for less
than two seconds use the short expiry. A newly positioned spawned light can take
over its recent matching entry within three metres when type, reach and color
agree. Map lamps and incompatible neighbors are excluded from that takeover.
Transient/carried lights use a 0.25-second hold and 0.3-second fade, rather than
the stationary-lamp persistence. The light-count and memory bounds are unchanged.

This is a direct engine correction, with no additional UI switch. It preserves
the earlier unconditional sunlight and lamp-fog corrections, VRAM fix, 4K menu
filtering/HUD, custom controls and current launcher. It does not import native
draws, online updates or an entire upstream release. The release baseline is
still 0.9.39 beta29.

## Validation and limits

- The expanded upstream lamp-fog suite passes with GCC 13, C++20, `-O2`,
  assertions enabled and `-Wall -Wextra -Werror`.
- It passes with AddressSanitizer and UndefinedBehaviorSanitizer. LeakSanitizer
  is disabled because process inspection is restricted in this environment.
- The same new assertions fail against the old header at the disappeared-torch
  expiry check. For that comparison only, the test's new `ShortLived` constant
  reference is replaced with its literal value 2.0; old behavior is unmodified.
- Tests cover stopped carried torches, fire flashes, neighboring map lamps,
  position jitter, changed color/reach, jumps and camera motion, alongside the
  existing selection, fading and memory-bound checks.
- Integration 60 verifies 2530 engine entries; formatting and scope checks pass.

The upstream flash heuristic requires brightness >=1.4 and recent disappearance
within 15 metres after a short listing. Dimmer thrown flashes can still persist;
this backport does not claim to classify every possible fire effect. Map lamps
still intentionally retain their anti-popping history. The user's particular
fire scene and the complete Windows engine build require local validation.

Close game and launcher, apply the source overlay on update 127, then run
`bash tools/build-better-kbm.sh`. A launcher-only rebuild cannot apply this fix.
Neither 4K assets nor saves need regeneration or deletion for this update.
