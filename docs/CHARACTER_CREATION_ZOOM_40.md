# Responsive character creation zoom

Integration 40, applied after Editor39.

Integration 39 added 120 ms of trigger input per mouse-wheel detent, with up to 600 ms queued. Fast scrolling therefore left the camera zooming after the wheel stopped. This delay was introduced by our input adapter.

The wheel now refreshes one 40 ms trigger pulse from the latest complete detent. No duration accumulates, regardless of scrolling speed or event size. Forty milliseconds spans the native menu's 30 Hz sampling interval. Direction reversal remains immediate on the next complete detent; fractional deltas still accumulate. The existing editor detection, gameplay wheel bindings, focus/capture cancellation, rotation and slider dragging are unchanged.

Validation: compiled and ran production Zoom/Rotation/Steps helper tests. The regression cases cover a 201-event burst, large deltas, the exact 40 ms stop boundary, direction reversal, fractional detents, and 34 sampling phases at 30 Hz. Existing rotation/slider timing cases pass. The production input-bridge test timing assertion is updated to the new pulse duration. Windows game verification is still required.

Build using tools/build-better-kbm.sh and keep BetterKBM-engine-update36 selected. The startup log must report BetterKBM native engine integration 40.
