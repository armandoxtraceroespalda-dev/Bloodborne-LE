# Beta31 scene-proxy lifetime fix only

This is a selective backport of Awakenchan's VRAM leak correction from
[`008208bea1895647f185303f71b02a53ea915a5d`](https://github.com/Awakenchan/LanternEngine/commit/008208bea1895647f185303f71b02a53ea915a5d)
(0.9.39 beta31). It does not update the renderer's draw path, cooperative play,
launcher appearance, saved-game tiles, or other beta30/beta31 behavior. The
upstream release baseline stays 0.9.39 beta29. Both existing fog fixes are kept.

## Cause and correction

Reduced-resolution render targets have a native game image and one or more
smaller proxy images. Deleting the original image did not remove its entries
from SceneTargets, retaining the proxy images, views and their VRAM.

TextureCache::DeleteImage now queues the original image's unique ID for release.
The queue is protected for concurrent producers and drained by SceneTargets::Get
on the rendering side. All 16 possible proxy mip levels for that ID are detached
from the map, tracking set and recent-entry pointer cache. The scheduler keeps
the detached entries alive until the GPU reaches the recorded CPU tick, then
destroys them. Other live images retain their proxies. The lifetime correction
does not change resolution selection, filtering, upscaler settings or image
quality. A cumulative diagnostic is printed after each additional 256 released
proxies; it reports their total allocation size and the remaining live count.

## Included source changes

- `scene_proxy_release.h`: upstream release queue and all-level extraction helper.
- `vk_scene_resolution.h/.cpp`: queue, safe removal and deferred destruction.
- `texture_cache.cpp`: deletion notification, including the delayed-image path.
- `test_scene_proxy_release.cpp`: original upstream CPU regression test.
- `test_scene_resolution.cpp`: original upstream GPU lifecycle assertions.
- `build.sh --test`: only the upstream scene-proxy test command is added; the
  adjacent native GX-record changes are intentionally not imported.
- Integration manifest 58 records the selected upstream paths and hashes.

## Validation

- Upstream `scene-proxy-release-test` compiles with GCC 13, C++20, `-O2`,
  `-Wall -Wextra -Werror`, assertions enabled, and passes.
- The same test passes with AddressSanitizer and UndefinedBehaviorSanitizer.
  LeakSanitizer is disabled because this environment prevents its process
  inspection; a GPU VRAM leak measurement is not claimed.
- The test checks 4000 IDs queued from four threads, all mip levels of one image,
  preservation of other images, destruction delayed until ownership is released,
  repeated/missing IDs, and bounded proxy ownership over 5000 recreation cycles.
- `bash -n engine/build.sh`, `git diff --check`, source-integration verification,
  and scope/preservation checks pass.

The GPU scene-resolution test is included but could not be executed here. A full
Windows engine link and Bloodborne gameplay still require local validation.
Close the game and launcher, copy this source overlay into the existing checkout,
and run `bash tools/build-better-kbm.sh`. Compare dedicated GPU-memory usage over
repeated area loads with the same render resolution/upscaler configuration.
The correction targets retained scene proxies, not the launcher's system RAM.
