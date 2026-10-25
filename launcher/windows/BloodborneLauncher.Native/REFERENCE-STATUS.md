# Reference fidelity status — revision 74

The reference is OUR customized WPF launcher, with the saved UI56–59 overlays.
It is not the unmodified author's launcher. Revision 63's Controls and Graphics
arrangement is retained; revision 67 replaces the other generic page arrangements.

## Translated page structure

- Game follows PlayPage: language selectors, game/DLC folders, Steam integration,
  player profile management, save folder, backups and save import. Paths/status
  legends are visible and long captions wrap rather than displace the actions.
- Online restores Play, Server, website, Hunter's Dream account, shadNet account
  and Connection. Account actions use the existing Core API; passwords continue
  through the source DPAPI accessor.
- Achievements adds source-weighted completion, base/DLC bars, grade counters,
  earned/locked filtering, timestamps, source-extracted TRP images (grey when
  locked), notification options, native sample banner/sound, log and reset.
- Settings restores the engine location and Files actions/icons, without a blank
  About section. It does not create updater/community controls.
- More restores centered Mods / Log / Engine navigation. The header remains
  fixed while its child content scrolls.
- Cheats keeps its stable page identifier but moves below Tweaks in navigation.
- Folder picking is IFileOpenDialog with FOS_PICKFOLDERS and FOS_FORCEFILESYSTEM;
  no SHBrowseForFolder tree, WinForms, or WPF dialog dependency remains.

TrophyIcons, SaveImport and SteamShortcut are source links, not copies delivered
inside this package. The native trophy sample uses a Win32 banner/MCI transport;
its two-note bell synthesis is adapted from the production Core/Chime.cs.

## Validation limits and remaining differences

Release compilation and 100 automated checks pass. They do not establish visual
parity: this environment cannot run the Windows HWND renderer or real dialogs.
The complete layout, DPI, notification and account interactions need Windows
comparison before calling this a finished 1:1 replacement.

Ordinary text/password settings and log selection now use native EDIT controls.
The launcher's Appearance/custom-icon editor is not yet implemented. The native sample banner is a new
frontend implementation and needs direct comparison with the WPF toast. Keyboard
navigation/accessibility and live game-state/Stop integration remain incomplete.
Game launch still uses the original --play backend; keep the WPF package.

The source reference and explicit visual-check status remain in
reference-baseline.json. The screenshot helper now covers every visible page,
not just Start, Graphics and Controls. It neither uploads screenshots nor starts
the game.

## Revision 65 page completion

More now has the original Mods and Output structure and the requested removal of
the extra patches-expansion step. Mods/patches use distinct list state, retain load
order and independent switches, and display folder legends with browse/open/refresh
actions. PatchTitles is linked from the checkout to preserve contextual labels.
Output follows the original session file, has Copy/Export/Clear and follow/start
switches, and draws only the viewport rows inside its independently scrolling box.
Launch includes the requested About legend and original/fork links.

66 C# checks and 19 Python checks pass; this does not replace Windows visual testing.

## Revision 66 interaction changes

Start dimming, retargetable transitions, native inline editing/output selection,
monitor-name overflow, in-launcher recommendation review and silent consistent
main-scrollbar dragging follow the latest requested behavior. The recommendation
ranks GPU model families and preserves Vulkan IDs; original hardware/upscaler
helpers still own the proposed graphics settings. The native EDIT surfaces need
Windows comparison, especially DPI, clipping and their entrance visibility.
CURSOL_CANCEL extraction was verified against the supplied Japanese game bank.
It is decoded at runtime in an isolated worker and is not shipped in this package.

## Revision 67 resource policy and output

Viewport-only native text resources replace whole-page native layout retention.
The drawing descriptions still contain off-screen rows so no controls/tree need
reconstruction during scrolling. Measurements reuse exact parameters while their
page remains active. Unused formats, pictures and book-brush references are disposed.
Native EDIT synchronization skips unchanged animation frames. These are ownership
changes, not forced heap/working-set cleaning. Actual Windows RAM still needs a
repeatable same-process measurement; the attached engine log cannot establish it.

Output now uses word wrap, native context menus and explicit wheel handling. It
prioritizes the active profile runtime log and filters launcher preparation only
for the combined-session fallback. A synthetic 5000-row ownership test and the
supplied session-log filter pass. Win32 context-menu/wheel interaction remains
a Windows validation task.

## Revision 68

Current-session game output, stable filtered keybinding geometry and selector
tooltip suppression are covered by automated boundary/geometry checks. Interactive
Windows comparison remains pending; no new pixel-parity or RAM claim is made.

## Revision 69

The reference wallpaper was centered. Native framing now slightly shifts the
artwork right using existing horizontal crop room; matching aspect ratios remain
uncropped. Additional resource-lifetime changes preserve approved effects.
No numeric Windows RAM reduction or new pixel-parity claim is made.

## Revision 70

Explicit navigation reclamation follows the user's requested policy. Resource
release still precedes collection and resident-page trimming. No appearance or
engine change is intended. Scheduler behavior is tested with injected operations;
actual Windows working-set reduction and transition responsiveness are unmeasured.

## Revision 71

Settings data, help metadata and back-sound extraction are deferred from startup.
Start summary parity is checked against upstream choices; no appearance change
is intended. Windows startup memory and first-use latency remain unmeasured here.

## Revision 72

Startup also requests one explicit memory-reclamation pass after its first valid
frame. No layout or engine change is intended. Windows RAM behavior is unmeasured.

## Revision 73: primary launcher

The native UI is now the default installed executable. The historical WPF project
forwards to it, and Play uses source-linked runner/environment services directly.
A fixture verifies runtime handoff without another UI process. This promotes the
user-approved appearance; it does not certify previously unmeasured pixel parity
or all Windows runtime behavior. The package README records migration/validation
limits and the requirement for the existing extracted gui cache.

## Revision 74: background notifications

An explicit game-event wake message bypasses the inactive rendering timer. The
producer/consumer regression is tested with a queued trophy and no UI polling.
Actual overlay presentation during Windows gameplay remains to be checked there.
