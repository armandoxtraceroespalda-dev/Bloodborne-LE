# PR #91: steady volumetric sunlight color

This fork backports [Awakenchan/LanternEngine#91](https://github.com/Awakenchan/LanternEngine/pull/91),
authored by **rhkim1292**, from commit
`f6f97d3baa1caa9df163e8fff0eb5e44eec23c76` in `rhkim1292/LanternEngine`.
The PR was open when reviewed; it is not represented as a merged upstream release.
The fork baseline remains upstream **0.9.39 beta29**, commit
`e6b943dd969d99561545315f3d801f10e4a6cf8d`.

## Engine behavior

The fog tint previously selected the brightest directional-light record every
frame. A default white record could win over the area's lights as table contents
changed with camera movement. The backport recognizes the PR's exact fallback
record and uses it only when no other light contributes. Other white lights are
retained. The selected color approaches its new target exponentially, with a
0.5-second time constant, and snaps after a gap longer than one second.

The shader and its host push constants both use the new 16-byte tint layout.
`BB_VOLUMETRIC_STEADY=0` restores the previous selection and immediate color
changes. The fix is enabled by default. Its setting takes effect on game launch.

## Launcher adaptation

Our native launcher exposes **Steady sunlight color** in **Graphics > Lighting**,
beside volumetric sunlight strength, rather than introducing another Graphics
group in More > Engine. It remains visible and becomes disabled when volumetric
sunlight is off. The existing global settings search indexes this option.
The source title and tooltip have entries in all 14 existing launcher catalogs.
The existing shared EngineSwitches contract carries the setting to the engine;
graphics profiles do not overwrite it.

The upstream implementation and GPU regression test are preserved. The Vulkan
header explicitly includes `<chrono>`. The original WPF-specific LooksChecks
assertions are covered by native `VolumetricSunlightTests` in this launcher.

## Rebuilding and updates

Copy the source overlay into the existing checkout and run:

```bash
bash tools/build-better-kbm.sh
```

Both the engine and launcher must be rebuilt. Integration manifest **55** records
the PR URL, author, pinned head/base commits and affected engine hashes. It adds
the GPU test file to verification without changing the upstream baseline.
Existing settings, saves, extracted assets, custom controls and renderer changes
are preserved. Generated binaries and extracted game assets are not source files.

## Validation

- Native .NET 8 Release build: zero warnings and errors.
- Native self-test: 491 passing checks, including translated UI, defaults,
  effect gating, search placement and the engine setting contract.
- Upstream `volumetric-tint-test`: passes through Vulkan on Mesa llvmpipe.
- The same test fails its color assertion with the original tint shader.
- `effect-pipelines-test`: 15 built, zero reported validation messages.
- `point-volumetric-test`: passes; maximum reference error 0.614% over 1881 values.
- Engine source verification: integration 55, 2523 tracked engine entries.

The Vulkan tests here use a software device, not the user's RTX 4090. A complete
Windows engine link and Bloodborne gameplay cannot be run in this environment;
the rebuilt package still needs an in-game visual check on the user's system.
