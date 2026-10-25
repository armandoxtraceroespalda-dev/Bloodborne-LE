## Integration 36 validation

The production input, pointer and camera tests now target upstream d901fbd2's
implementations with BetterKBM extensions, rather than the superseded bridge.
`test_camera_stub.cpp` executes the current native camera assembly trampoline.
`play_backups/PlayBackupsTests.csproj` covers actual quiet save copies, unfinished
transactions, duplicate suppression, independent retention and unchanged sources.

```sh
python3 tests/better_kbm/run.py --engine engine --sdk
# SDL3 headers must be discoverable, or pass --sdl-include explicitly.
dotnet run --project tests/better_kbm/play_backups/PlayBackupsTests.csproj -c Release
```

## Integration 17: upstream and output synchronization

The normal runner also tests the production settings serializer/importer,
external graphics edits, pending local values, comments/unknown keys, custom
output dimensions, startup snapshots and native live-resolution policy.

```bash
python3 tests/better_kbm/run.py --engine ../BetterKBM-engine-expanded
dotnet run --project tests/better_kbm/ini_sync/IniSyncTests.csproj -c Release
```

The launcher INI fixture covers external edits, default binding deletion, atomic
saves and retry after failure. No game assets are included. These fixtures do
not replace a real Windows/Vulkan gameplay test.

## Current validation: UI32 navigation memory and dark scrollbars

UI32 replaces UI31's automatic-only runtime policy with one pressure-gated
post-transition attempt. It does not restore the old periodic/input/saves
policies. Run navigation_memory/NavigationMemoryTests.csproj for settled batch,
coalescing, busy skip, cooldown/backoff and the actual collection primitive.
The collection fixture measures a Linux synthetic heap, not Windows launcher
RAM. launcher_memory validates actual IL: the only induced-GC site is
NavigationCollection.Release; no finalizer wait or old cleanup types ship.

launcher_shell now also validates real native vertical/horizontal PART_Track,
correct direction and page commands, two-way values and the dark normal/hover/
drag palette. It cross-compiles here; rendered Windows tests remain pending.
The UI31 resource ownership and allocation fixes remain unchanged.

For Windows comparisons run tools/measure-launcher-retention.ps1 normally and
again with -AutomaticGcOnly, using separate report subfolders under
launcher-retention-report. The sampler does not collect; it observes the
selected executable policy. The optional -CaptureHeap still explicitly induces
two diagnostic collections and belongs in a separate root-graph run.

Earlier revision validation is historical and follows below.

## Current memory validation: UI31

Normal launcher execution no longer induces GC or trims the process working set.
The three earlier reclamation classes remain in the overlay for historical
review but are excluded from the launcher project. Their old standalone policy
fixtures below describe superseded experiments, not current runtime behavior.

Current checks:

```bash
dotnet run --project tests/better_kbm/passive_resources/PassiveResourceTests.csproj -c Release
dotnet run --project tests/better_kbm/launcher_memory/LauncherMemoryTests.csproj -c Release -- "path/to/Bloodborne Launcher.dll"
dotnet run --project tests/better_kbm/launcher_diagnostics/LauncherDiagnosticsTests.csproj -c Release
dotnet run --project tests/better_kbm/menu_audio/MenuAudioTests.csproj -c Release
```

The passive-resource fixture checks real PCM array ownership and complete
cleanup after one disposal action fails. The metadata fixture rejects GC.Collect
and GC.WaitForPendingFinalizers references and obsolete cleanup types in the
actual launcher DLL. Diagnostics are allocation-free when disabled; repeat that
fixture with BB_LAUNCHER_UI_TRACE=1 to check real measurement intervals.
The native SDL fixture now checks changed buttons/axes and 10,000 warm activity
polls with no per-poll managed allocation, as well as unload/reload ownership.

On Windows, launcher_shell checks actual SoundPlayer cache reuse after the
import stream releases its samples and cancellation of transition clocks and
callbacks. launcher_bitmap checks detached pixels/rendering and absence of a
full-resolution managed staging array. Linux cross-compilation does not execute
these WPF fixtures or measure actual Windows working-set savings.

Use tools/measure-launcher-retention.ps1 for a separate Windows observation run.
The optional -CaptureHeap switch requires dotnet-gcdump and explicitly induces
two diagnostic collections. Do not mix that run with the ordinary memory/time
comparison. Runtime code never invokes that tool or collects on pointer input.

Historical implementation notes follow.

# Integration checks

After applying the cumulative engine patch:

```bash
python3 tests/better_kbm/run.py --engine ../BetterKBM-engine-expanded
python3 tests/better_kbm/run.py --engine ../BetterKBM-engine-expanded --sdk
```

The first command compiles and executes the actual input, bindings, capture and pointer sources with mocked SDL/guest services. It exercises diagonal sprint, world movement versus title navigation, keyboard/controller coexistence, separate E/R/C actions, Options/Back, chord precedence and release, forward/reverse requests, mouse clicks and cursor timeout.

`--sdk` additionally requires the native build dependencies (SDL3 headers through pkg-config, zlib, C/C++ compilers). It tests the production controller queue and persistence, real movie serialization over a synthetic two-frame layout, the native message converter and eight-language BND/FMG/DCX localization. No game assets are needed. If SDL3 is not registered with pkg-config, pass `--sdl-include /path/to/include` explicitly.

An optional `--game /path/to/plaintext/eboot.bin` validates the original 1.09 signatures, vanilla section handlers, camera fields, child stack, native Defaults confirmation and quick-item entry points. It reads the user's local file without modifying it.

These checks do not launch the game, exercise a real Vulkan driver or replace an interactive Windows play test.

## Windows runtime deployment

From MSYS2 CLANG64, test the runtime deployer independently:

```bash
python3 tests/better_kbm/test_runtime_deployment.py --runtime-dir /clang64/bin --objdump llvm-objdump
```

This cross-links real x64 Windows PE fixtures, checks transitive runtime copying and case-insensitive DLL names, verifies repeated runs preserve output, and checks missing-library and malformed-image diagnostics. The fixtures are never executed.

## Native menu update checks

`--sdk` also tests the actual runtime Quit to Desktop state machine, including
mounted saves, the title fallback, cancellation and expiry. Pointer checks cover
left/right selector hit regions and binding-row capture.

With a locally built converter and your own game dump, optionally run:

```bash
python3 tests/better_kbm/test_game_menus.py --engine ../BetterKBM-engine-expanded --converter ../BetterKBM-engine-expanded/out/bb-engine-assets.exe --game-dir /path/to/game
```

This checks the real movie layout, independent Controls/Graphics frames, native
action-button columns, Defaults, title/System quit rows, and localized bundles.
It reads your game files and writes only temporary outputs. No game data is
included in the test suite.

The real-menu check also verifies both mirrored texture bounds for all five
resized frames, independent frame characters, unchanged text/widget matrices
across the animation timelines, Key Bindings row spacing, and Defaults placement
below the compact Effects/Image settings borders.

Image/Camera revision: the real-menu test checks a four-setting Image section,
a three-setting Camera section, six aligned private frames, Defaults outside
both settings regions, and an unchanged original Environment sprite. The main
test runner also checks mirror cache hits skip transformation, while corrupted
cache output regenerates and modified source layouts are rejected.

Resolution/input revision: tests check the real resolution widget's private
opaque RGBA background, display depth above the action buttons, unchanged
animation matrices, native tick priority, T before a profile is reapplied, and
preservation of explicit custom keyboard bindings.

## Launcher chord capture

With .NET SDK 8 or newer:

```bash
dotnet run --project tests/better_kbm/launcher_capture/LauncherCapture.csproj -c Release
```

This dependency-free console suite compiles the production BindingCapture class.
It covers a first key before any timer tick, chords with staggered release,
additional inputs while a chord is held, opening-gesture exclusion, wheel pulses,
release fallback, cancellation and the next capture. Interactive WPF validation
must also check focus after clicking a binding, Tab/Alt, the capture pad and
Cancel/Unbind in the launcher's actual window.

Sprint/controller camera revision: the normal test runner now executes the
production AVX store-hook assembly in a synthetic camera frame. It verifies
that disabled mouse input cannot call the host callback or alter controller
pitch/yaw, while enabled mouse input still affects both axes. Input bridge
checks cover ordered keyboard rollover, repeat rejection, preserved held sprint,
diagonal additions, every controller axis pair and controller priority over a
pending mouse target-switch flick. These tests require an x86-64 CPU with AVX;
they do not assert that the game's final skid animation has been play-tested.

Compatible sprint turns: the normal runner checks the production sprint-turn
policy across all eight movement targets at six update rates. Compatible turns
retain a fully pushed axis and cap per-sample rotation; opposing-axis changes
snap immediately. Tests also cover mid-turn reversals, release without a delay,
non-sprint passthrough and multiple samples at the same timestamp. The numerical
invariants do not prove the game's final sprint/animation state.

Movement tuning: the normal runner checks configurable rates, 60 ms release
grace, expiry, reversals during grace, immediate sprint release, circular range,
and zero-rate instant turns. It also compiles the production settings reader/
writer and checks defaults, save/reload, bounds and preserved bindings. Launcher
cache behavior and UI responsiveness need an interactive Windows WPF check.

## Shared camera ownership and regional text

The normal runner checks the production shared-camera helpers: a stack-local
factory record is rejected, all 88 valid menu/serialized layouts agree, and
unchanged parent menu snapshots cannot replay older settings. Individual edits
and native Defaults changes are tracked per field. The optional game-signature
check verifies the live options pointer and both callers of the record copier.

The actual guest file-open/read/stat suite also mounts synthetic Spanish text
bundles and verifies Latin American and Spain selections independently of voice
selection, including exact localized menu mounts. The localization suite checks
Spanish Image labels/help and the requested Bloom / Motion Blur names for both
Spanish locales. These fixtures do not establish differences in the original
game's regional wording and do not replace a Windows gameplay test.

## Startup diagnostics regression (UI 11)

```bash
dotnet run --project tests/better_kbm/launcher_diagnostics/LauncherDiagnosticsTests.csproj -c Release
```

Exercises real process allocation intervals, exception/idempotent cleanup, startup
log rotation, previous-run preservation, bounded log size and nonfatal I/O errors.
No forced GC is used. Windows bitmap tests additionally cover read-only alpha
sampling of opaque, transparent and mixed images. Run those on Windows; a Linux
cross-build does not validate WPF rendering or actual Windows memory usage.

## Asset worker and sidebar bounds (UI 12)

```bash
dotnet run --project tests/better_kbm/asset_worker/AssetWorkerTests.csproj -c Release
dotnet run --project tests/better_kbm/launcher_memory/LauncherMemoryTests.csproj -c Release
```

The worker fixture exercises real process launch, argument quoting, progress,
failure/incomplete output, cancellation/reaping, staging cleanup and cache
preservation. It models the extractor's output contract rather than converting
actual game textures. The production game extractor has separate tests.
The glow checks assert caption-relative, inset bounds at multiple row sizes.

## Local title icon and cache upgrade (UI 14)

```bash
dotnet run --project tests/better_kbm/game_menu_assets/GameMenuAssetsTests.csproj
dotnet run --project tests/better_kbm/game_window_icon/GameWindowIconTests.csproj
python tests/better_kbm/test_launcher_gui_preservation.py
```

The extractor fixture includes multiple textures in a single PS4 TPF, checks
that the PNG-backed ICO preserves the extracted pixels, and upgrades an older
cache missing the icon. Game-window targeting checks multi-level child processes,
unrelated processes and malformed parent cycles. GUI preservation includes both
icon formats on successful and failed publishing. These checks do not exercise
Win32 icon messages or WPF rendering; verify those on Windows.

## Optional local menu audio (UI 15)

```bash
dotnet run --project tests/better_kbm/menu_audio/MenuAudioTests.csproj
python tests/better_kbm/test_launcher_gui_preservation.py
```

Checks cue precedence, duplicate routing suppression, rapid hover bursts, protection
of the Play cue and bounded PCM16 WAV validation. GUI publishing also preserves
user-provided hover/select/start WAV files. No game audio or synthetic fallback
is shipped. These portable tests do not verify WPF pointer events or audible
Windows playback; both need a Windows check with the user's actual clips.

## Automatic native menu sound extraction (UI 16)

```bash
dotnet run --project tests/better_kbm/menu_sound_extraction/MenuSoundExtractionTests.csproj
```

Synthetic PCM16 FSB5 fixtures exercise cache hits without source banks, missing
clip repair, preservation of user-supplied WAVs, source-file immutability,
language-bank fallback, cancellation and real worker success/failure/cancellation.
No proprietary audio is part of these fixtures.

To inspect your own original banks separately:

```bash
dotnet run --project tests/better_kbm/menu_sound_extraction/MenuSoundExtractionTests.csproj -- --real /tmp/bb-menu-check /path/to/sprj_main_eng.fsb /path/to/sprj_main_esa.fsb
```

This optional mode decodes the four named clips from each supplied bank and
compares the resulting PCM hashes. Eight original regional banks were validated
for UI16. Codec tests do not replace a Windows check of actual pointer events
and audible playback. The pinned MIT-licensed ATRAC9 subset includes the .NET8
full-read compatibility change documented in its README.

## UI17 audio and memory update

menu_audio now checks PCM attenuation at 35%, header integrity, signed samples,
zero/unity gains, invalid gains and idle collection thresholds/cooldown.
menu_sound_extraction covers the new confirm.wav/menu_open mapping and preserves
legacy select.wav during cache upgrades. All eight real localized banks were
checked locally; their audio is not included in the test suite or source kit.
launcher_bitmap additionally verifies detached premultiplied caching; it needs
Windows WPF and was not executed on the Linux build host.

## UI18 option controls

menu_audio uses MenuWave.LauncherGain to validate the current 25% attenuation
without changing cached native WAV samples. Source package resource curation
was checked against WPF-UI 4.3.0 embedded BAML names and upstream XAML resource
references. Responsive Start, More hit areas, switch coloring/audio and actual
RAM must be exercised on Windows; these are not claimed as Linux UI tests.

## UI20 renderer preference

Run `dotnet run --project tests/better_kbm/launcher_render_policy/LauncherRenderPolicyTests.csproj`.
The test links production LauncherRenderPolicy and validates the lower-memory
normal preference, explicit default-rendering fallback and distinct audit modes.
It does not assert an absolute RAM target or interactive performance on Windows.

## UI21 native SDL lifetime and navigation reclamation

`tests/better_kbm/sdl_lifetime` links the production SdlPad bridge. Build native.c
as a shared library named SDL3.dll in an isolated fixture directory (on Linux,
`cc -shared -fPIC native.c -o SDL3.dll`). Set SDL_LIFETIME_LOG to an isolated log,
then run the SdlLifetimeTests project with that directory as its argument. A
second fixture built with -DOMIT_EXPORT is tested with --broken. Test outputs,
native binaries and logs are not part of the source kit.

The native fixture tests 25 reload cycles, held input values, correct handle /
subsystem / full SDL / module shutdown order, idempotence and failure cleanup.
It is not a real Windows SDL test. menu_audio now validates the 8-MiB / eight-
second collection thresholds and remaining cooldown calculation. Windows UI
transition cancellation, real controllers and memory still require Windows.

## UI22 excess GC commit after navigation

Run `dotnet run --project tests/better_kbm/heap_release/HeapReleaseTests.csproj -c Release`.
The production policy is linked directly. It checks supplied-log thresholds,
quiet input, busy UI, cancel, one-shot behavior and cooldown, then allocates an
isolated fragmented heap and verifies actual decommit with live buffers intact.
The fixture intentionally allocates about 128 MiB; run separately from any real
launcher memory measurement. It is not an interactive Windows/WPF benchmark.

## UI23 reduced navigation allocation paths

Run `dotnet run --project tests/better_kbm/binding_geometry/BindingGeometryTests.csproj -c Release`.
The production hit/draw geometry and click routing are linked directly. Tests
cover narrow and wide layouts, long chords, wrapping, exact fit, per-binding
removal, help targets, modified markers and cancellation when dragging off x.
Existing launcher_capture tests retain the production capture state machine.
On Windows, launcher_shell additionally checks detached section preparation,
idempotence, no binding-cell template expansion, native button automation and
retained overlay ownership. It is cross-compiled but not run on Linux. These
regressions do not assert a numerical Windows memory saving or a 60 MB ceiling.

## UI24 live output and option sounds

Set BB_KBM_TEST_ENGINE to the updated engine directory, then run
`python3 tests/better_kbm/test_live_resolution.py` for the real Windows runner
resolver. The GPU process is mocked; the native 1080p/TAA configuration and
explicit/resolved flags are real inputs. Build test_output_resolution.cpp with
the production bbport_settings.cpp and bbport_menu_i18n.cpp and gpu/shim include
path for Off/On/Auto, every output, pending save/restart, preserved binding,
fixed guest dimensions and TAA regression coverage. The CMake upscaler-support
fixture now declares DLSS availability and expects the existing FSR 4.1.1 ->
FSR 4 fallback, rather than the older FSR 3.1-only capability expectation.

launcher_shell also checks actual attached-property defaults for binding cells
and More's nested buttons. It must run on Windows STA; cross-compilation does
not execute its WPF assertions. menu_audio and heap_release retain the existing
25% PCM, cue scheduling, retired-view and idle decommit regression paths.

## UI25 hardware-aware upscaler recommendations

```bash
dotnet run --project tests/better_kbm/hardware_recommendation/HardwareRecommendationTests.csproj -c Release
```

Links the actual hardware/recommendation, Vulkan adapter selection, choices,
settings, INI and localization sources. Exercises NVIDIA RTX vs GTX, AMD RX 9000
vs older/integrated GPUs, Intel/unknown, matching and incomplete FSR4 assets,
DLSS paths, launch environment precedence, proposal/apply and unrelated-value
preservation. Driver discovery caches are seeded, so Windows graphics drivers
are not loaded; this is not a live GPU capability or performance benchmark.
Native engine integration remains 16. Visual verification requires Windows.

## UI26 post-transition reclamation

```bash
dotnet run --project tests/better_kbm/heap_release/HeapReleaseTests.csproj -c Release
dotnet run --project tests/better_kbm/launcher_memory/LauncherMemoryTests.csproj -c Release
```

The production heap-policy regression also covers navigation debounce/coalescing,
hover independence, save priority, capture/modal deferral, cancellation, short
navigation cooldown, discarded unswept views and live-object preservation.
Settings-only idle behavior remains covered. Actual GC integration uses a
synthetic process heap; it is not a Windows/WPF benchmark. Main-window wiring
and transition completion cross-compile for Windows. Native engine unchanged.

## UI27 startup resources and shared text language

Run the startup_resources and localization .csproj fixtures. They link production
sources and cover DPI/crop-aware bounded backdrop sizes, WAV header-only cache
validation with counted reads, corrupt headers, immutable fallback-font extraction,
all nine existing text-language IDs, the complete eight-catalog key set, .NET
format placeholders, live language switching and unchanged process culture,
user paths and binding values. Spanish regional game IDs share one launcher
catalog. Voice settings remain independent in game_language_settings.

launcher_shell additionally cross-compiles the native Window/base-control shell,
absence of the WPF-UI dependency and Start's shell-free construction. Its WPF
assertions require Windows STA and were not executed on Linux. Re-run normal
launcher_memory, menu_audio, heap_release, hardware_recommendation and capture
fixtures. The existing GUI-preserving publisher regression remains applicable.

The bundled font is a renamed Noto CJK subset under Fonts/OFL.txt. No original
game art, font or sound is shipped. Runtime Windows resident RAM, visual layout,
Japanese/Cyrillic rendering and audible modal controls require a Windows check;
these fixtures do not prove a fixed memory ceiling or measured WPF RAM savings.

## UI28 literal choices and interaction memory

interaction_memory links production InteractionHeapReclaimer/IdleHeapReclaimer.
It checks a million allocation-free input notifications, meaningful allocation /
managed-growth thresholds, excess GC commit, quiet and continuous bursts,
capture/modal deferral, cooldown and no-progress backoff, cancellation and a
new active-page baseline. A synthetic discarded-buffer fixture uses the actual
compacting release and verifies live buffers survive. It is not a Windows WPF
working-set or hover benchmark. Existing hardware_recommendation also tests
literal device labels and invariant endonyms across all nine text languages.

Monitor now lives in Display > Display & FPS before Show FPS. Native UI tests
still need Windows: verify its style, saved device / disconnected-device behavior,
endonyms in both language lists, modal/capture sound and smooth hover/drag.
For optional process/managed/GC attribution, launch with
BB_LAUNCHER_HOVER_TRACE=1 and inspect launcher-diagnostics.log after moving only
inside one control, then across multiple controls, then pausing. Compare muted
and unmuted runs. Default operation does not log pointer events. A working-set
increase without managed growth is not attributed to managed garbage by the
policy; active resources and native caches must be profiled separately.

## UI29 context review and periodic reclamation

The localization fixture also compares 2,597 curated values/case variants with
review files for game actions, controls, graphics and general UI. It explicitly
covers both Spanish IDs, Action, movement labels, the search hint and Monitor &
Info, and rejects HTML entities in plain interface text. Full catalog coverage
and .NET placeholders remain checked. These are regression tests, not native-
speaker certification of every diagnostic sentence.

interaction_memory now exercises periodic maintenance without pointer input,
lower meaningful-growth thresholds, short cooldown/backoff, bounded continuous
motion and capture protection. The UI uses a single 500-ms background check in
all visible pages; collection still requires meaningful pressure. The actual
synthetic release preserves live buffers. No Windows working-set ceiling or
interactive smoothness is asserted. Hover diagnostics remain available.

## UI30 search field, controller labels and icon ownership

The localization fixture now checks curated physical controller labels across all
text languages, including both Spanish selections. Options, Share and other
branded buttons remain literal; shape and directional names retain controller
meaning. The Windows-only `launcher_shell` fixture measures the real WPF search
viewport/caret for short and long input, placeholder transitions and multiline
editors. It also verifies the nested Cheats tree and separate compiled launcher
icon resource. Cross-compilation on Linux does not execute that STA fixture.

To replace the launcher's icon, edit
`launcher/windows/BloodborneLauncher/app.ico` and rebuild the launcher. The
separate `launcher/windows/LauncherBuilder/app.ico` belongs to the build utility.
The locally extracted `gui/game-icon.ico` is applied only to the launched game.

Integration 18: `dotnet run --project tests/better_kbm/engine_defaults` exercises
production launcher settings and environment generation against an older saved
configuration, including one-time performance-default migration, native-call
switch precedence, preserved user choices and safe mode. The native suite also
covers shared graphics settings, fixed/live output dimensions and controller
camera gating against the beta 5 engine snapshot.

Integration 20 adds `dotnet run --project tests/better_kbm/pad_menu_bindings`:
production PadMap/IniFile code verifies independent menu/gameplay mappings,
within-menu swaps, save/load, profile round trips and factory defaults. The native
pad fixture covers export/reset of both new menu slots. The upstream native
`tests/test_pipeline_archive.cpp` exercises beta 6's pipeline-archive growth fix.
