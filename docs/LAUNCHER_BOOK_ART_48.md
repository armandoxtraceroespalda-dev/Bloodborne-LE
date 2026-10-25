# Launcher book artwork (UI 48)

This launcher-only delta follows native integration 47. It does not change the
engine, keybindings, settings or selected engine directory.

## Artwork and extraction

The player's annotated MENU_Common_00001 atlas is 2048x2048. Exact regions:

| Output in gui | X | Y | Width | Height |
| --- | ---: | ---: | ---: | ---: |
| menu-sidebar-underline.png | 480 | 1210 | 350 | 22 |
| menu-book-leaf.png | 0 | 1088 | 472 | 936 |

These are extracted locally from dvdroot_ps4/menu/common.tpf.dcx using the
existing isolated asset worker. The original opaque black additive padding is
converted to transparency by the existing PNG writer. No original/cropped game
art is included in the source package. Existing valid GUI caches upgrade only
these two outputs and their hash manifest, preserving other GUI bytes and dates.
The cache is validated normally afterwards; no extraction runs every navigation.

## Presentation

- Sidebar labels use the marked ornate underline close to their text. Start is
  explicitly excluded through an attached property. Existing glow remains.
- A MenuBookHost wraps only the options column. Start gets its original full
  layout and no book. Sidebar and Windows caption controls remain outside it.
- One frozen leaf image is rendered on both sides, with the right side mirrored.
  Brush opacity is 0.5. Two horizontal slices preserve the binding width while
  the quiet page area stretches to fill large windows.
- The actual child layout is measured and arranged inside an ink-safe rectangle:
  binding/side margins plus top/bottom margins keep content and scrolling within
  the paper's inner area. This is layout reservation, not a visual overlay that
  covers existing labels. PageFrame clipping also contains transition movement.
- No full-window opacity mask, blur, texture atlas retention, additional page
  cache or periodic memory policy is introduced. The two crops retain roughly
  1.72 MiB of uncompressed pixels, shared between both sides.

## Validation

- DDS decoded and exact crops inspected against the player's red/green arrows.
- Transparent-black conversion and mirrored 50% composition inspected at
  900x620, 1280x800 and 1920x1080.
- Ink-safe rectangles checked at five content sizes, including small windows.
- Modified XAML parses successfully; Start exclusion and migration wiring checked.
- Existing asset test fixture updated with the common atlas and assertions that
  GUI upgrades preserve unrelated file contents and timestamps.
- This Linux workspace has no .NET SDK/WPF runtime. The C# extraction test and
  Windows launcher build were not executed here. The images used for inspection
  are layout studies, not screenshots of a running WPF launcher.

## Build and test on Windows

Close the launcher, copy ARCHIVOS over the working clone, then run:

    bash tools/build-launcher-only.sh

Only the launcher needs rebuilding. On its next launch the known game folder is
used to extract missing crops into LauncherApp/gui. Existing native integration
47 and BetterKBM-engine-update36 remain selected. The asset log identifies UI 48.

To run the updated extraction test in an environment with .NET 8:

    dotnet run --project tests/better_kbm/game_menu_assets
