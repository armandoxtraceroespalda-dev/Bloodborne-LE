# Bloodborne Launcher — native primary UI (revision 74)

Build from the checkout with `bash tools/build-launcher-only.sh`. The installed
entry point is `LauncherApp/Bloodborne Launcher.exe`. The older native-preview
command is a compatibility alias. Close all launcher processes before installation.

The Direct2D/DirectWrite/Win32 UI is the only default launcher implementation.
The historical WPF project path forwards Build/Restore/Publish/Clean to the native
project, and the solution/builder use that project. Historical WPF source remains
for comparison and shared source contracts; its UI is not compiled or launched.

Play uses the source-linked GameRunner, GameEnvironment, GameWindowIcon,
WindowsHdr, SessionStats, CheatLaunchNotice and existing Core services directly.
There is no old-launcher --play delegation. Configuration is merged into the real
INI before launch; source helper calls temporarily use actual runtime paths, then
restore the isolated editing context. The game-output file excludes launcher-only
preparation records. Runtime events are marshalled back to the UI; real trophy
progress and the synthetic cheat notice are separate. The direct --play mode
minimizes after successful launch and closes the UI on a normal game exit.

The installer preserves extracted gui and settings, atomically replaces the primary
executable, and archives old UI entry points with non-executable suffixes. This
migration expects the already working gui cache; `--assets` can name another local
extracted folder. No original game assets are bundled or uploaded.

Controls, approved theme/effects, lazy Start initialization, viewport resource
lifetime and explicit startup/navigation memory reclamation are retained. The
latter trims resident memory and does not impose a committed-memory/MB ceiling.

Run `--self-test` for managed checks. Linux can additionally compile
`tests/native_preview/user32_fixture.c` as a shared library and set
`BB_NATIVE_TEST_USER32` to exercise a harmless shell session against the original
runner. This test stub is never used by normal Windows execution. Source drift
checks use `tools/audit-native-upstream.py`. See the package README for validation,
migration paths, backups and the still-required real Windows game test.

## Revision 74

Game-session events actively wake the window through a dedicated WM_APP message.
They no longer depend on Tick, which is deliberately stopped while the launcher
is inactive. The producer queues first, then signals; the window subscribes once
and unsubscribes on disposal. Notification presentation still runs on its UI thread
and remains non-activating. This also delivers session-exit events while minimized.
The rendering timer and memory policies are unchanged. See the package README for
regression checks and Windows presentation limits.
