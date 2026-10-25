# Appearance preview zoom prompts (integration 47)

The appearance key guide previously overrode shoulder icons L1/R1 while native
ChrMake_BG zoom actually uses trigger icons L2/R2. Those triggers therefore
fell through to gameplay bindings and showed Ctrl/Shift instead of the wheel.

The preview-only resolver now maps L2 to the existing wheel-up mouse icon
(button 6) and R2 to wheel-down (button 7). Keycap::DrawMouse already draws the
highlighted wheel and a directional arrow, so no game texture replacement or
new icon asset is necessary. Outside the preview the normal gameplay bindings
remain authoritative. Gamepad prompts retain their existing device selection.

Validation: compile and execute the actual production IconSpec function against
mocked binding services, checking preview direction, gameplay context and pad
mode. Full source manifest verification passes. Real-game verification remains
for the player. Build with tools/build-better-kbm.sh after applying integration
46; retain BetterKBM-engine-update36 selected and check integration 47 in the log.
