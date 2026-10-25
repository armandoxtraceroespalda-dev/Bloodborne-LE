# Upstream integration boundary

The native project directly links Core modules from the checkout. In addition to
settings, controls, online accounts, profiles, patches, backups and localization,
the primary UI now links the original GameRunner/GameEnvironment and session,
HDR, icon and cheat-notice modules. No engine implementation is copied into the UI.

Host adapters supply mutable editing/runtime paths, the active INI, a directory-only
MenuAssets adapter and bounded diagnostics. Engine preparation remains the source
runner's bb-start/run_windows path with the original environment builder. The UI
stages changes, merges edited keys into current real files, uses the runtime paths
while starting, then restores its isolated editing paths. No legacy UI exe is used.

SteamShortcut.Add gains one optional launchOptions argument (default empty) so
existing callers remain compatible and the sole launcher can use --play directly.
This is the only modified existing Core service in this overlay. Shared SourceStamp
now fingerprints native sources plus linked original Core/localization/font/codec
sources. The old project file is only a target-forwarding compatibility entry.

For upstream updates, keep the author's Core file changes, rebuild, and inspect
`python3 tools/audit-native-upstream.py --root . --output LauncherApp/upstream-audit.json --strict`.
The audit hashes linked modules and compares graphics options with source views.
New UI rows and changed service signatures still need native schema/adapter review;
this is not an automatic merge of every future UI feature. Engine changes remain
independent of the launcher promotion. Historical views stay available for review.

Use build-launcher-only.sh for the default UI. Existing full-engine scripts call
this command. Tests and a Windows x64 publish were checked, but actual game/GPU,
HDR, notifications and installer behavior must still be tested on Windows.
