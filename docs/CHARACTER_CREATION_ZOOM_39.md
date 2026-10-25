# Character creation mouse-wheel zoom

Integration 39, applied after Editor38.

- Wheel up sends native L2 input to zoom in; wheel down sends native R2 input to zoom out.
- Both digital trigger bits and analog trigger values are delivered.
- Each detent remains held for 120 ms so frequent pad reads cannot consume the input before the native preview update. Queued input is bounded to 600 ms; reversing the wheel changes direction immediately.
- Fractional wheel deltas accumulate until a full detent is available.
- The mapping is scoped to the detected appearance editor, including the Dream mirror. Gameplay item and Shift+wheel weapon bindings are preserved.
- Focus loss, text entry, input capture, disabling mouse menus, and leaving the editor cancel pending zoom.

Validation: production input bridge tests cover both trigger directions, Shift held, fast polling, fractional deltas, text entry, existing gameplay wheel bindings, preview rotation, and slider dragging. Timing helper tests cover bounded accumulation and immediate reversal. Windows gameplay verification remains necessary.

Build with `bash tools/build-better-kbm.sh`. Keep the selected engine folder at `BetterKBM-engine-update36`; its name is retained to reuse the installation. The session must report `BetterKBM native engine integration 39`.
