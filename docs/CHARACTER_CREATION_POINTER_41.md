# Appearance editor pointer input and 80 ms zoom

Integration 41, applied after Editor40.

- Wheel zoom uses an 80 ms pulse from the latest detent; duration does not accumulate.
- Creator hit testing falls back to the visible row when its Cursor clip is empty or narrow, and includes arrow clips outside the slider bar.
- Arrow resolution supports Widgets/Slider/LeftArrow and RightArrow, Slider/LeftArrow and RightArrow, and direct LeftArrow/RightArrow children. The actual visible clip bounds are used.
- Editor mouse clicks are delivered as short native pad presses with a release between clicks, rather than disappearing after a single fast pad read. Each arrow click emits one directional press below the native held-key repeat delay. The game retains ownership of values, limits, and notifications.
- Explicit editor clicks can change rows while a previous synthetic direction press is ending.
- Queued clicks are cleared when input is blocked, including focus loss, text entry, overlay capture, and binding capture. Mouse-only behavior remains scoped to the appearance editor; gameplay mappings and physical gamepad handling are preserved.

Evidence and validation:
The supplied log confirms integration 40 and an active Dream mirror editor. It does not contain per-click hit-test traces, so the exact live widget hierarchy was not observed. The implementation covers the known native Slider/Widgets structures and row bounds; gameplay validation remains required.

A synthetic guest row with an empty Cursor reproduces a selection failure in the previous pointer code and passes with this change. Production pointer tests exercise visible-row selection, direct slider arrows, sustained click output, cancellation and existing menu navigation. Timing tests verify three distinct click edges across 34 native 30 Hz sampling phases and zoom stopping at 80 ms without a backlog. The production input bridge and Windows-target pointer syntax are checked separately.

Build: bash tools/build-better-kbm.sh
Keep BetterKBM-engine-update36 selected. The session must report BetterKBM native engine integration 41.
