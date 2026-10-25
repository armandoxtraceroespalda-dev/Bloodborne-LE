// SPDX-License-Identifier: GPL-3.0-or-later
#include "input.h"
#include "bindings.h"
#include "ini_bindings.h"
#include "chords.h"
#include "wheel_buttons.h"
#include "editor_drag.h"
#include "menu_keyboard_policy.h"
#include "pc_controls.h"
#include "../engine/item_cycle.h"
#include "../engine/option_menu.h"
#include <array>
#include <chrono>
extern "C" uint64_t runtime_pad_bindings_serial();
extern "C" int runtime_pad_export_binding(int,char*,size_t,char*,size_t);
#include "menu_pointer.h"
#include "mouse_camera.h"
namespace BbOverlay { bool MenuOpen(); bool CapturesInput(); }
#include "../bbport_settings.h"
#include "../engine/change_appearance.h"
#include <SDL3/SDL.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

namespace {
using namespace PadBits;

std::atomic<bool> g_active{false};
std::atomic<InputDevice> g_device{InputDevice::KeyboardMouse};
std::atomic<bool> g_focused{true}, g_in_window{true}, g_text_entry{false};

std::atomic<bool> g_relative{false}, g_wants_relative{false};

struct Mouse {
    float x = 0, y = 0;
    int window_w = 1920, window_h = 1080;
    std::uint32_t held = 0;
    std::uint32_t pressed = 0;
    float wheel = 0;
    float drag_x = 0, drag_y = 0;
};
std::mutex g_mouse_lock;
Mouse g_mouse;

std::mutex g_pad_lock;
PadState g_last_pad;

std::uint64_t g_saved_serial = 0, g_saved_pad_serial = 0;
std::array<bool, 512> g_event_keys{};
WheelButtons::Queue g_weapon_wheel;
std::atomic<bool> g_keyboard_moving{false};
bool g_previous_item_was = false;

constexpr float kPreviewLeft = 1028, kPreviewRight = 1810, kPreviewTop = 117, kPreviewBottom = 895;

constexpr std::uint32_t kLeftButton = SDL_BUTTON_LMASK, kRightButton = SDL_BUTTON_RMASK;

constexpr std::uint32_t kWheelUpInput = 1u << 5, kWheelDownInput = 1u << 6;

const char* ConfigPath() {
    const char* path = std::getenv("BB_CONFIG");
    return path && *path ? path : "lantern.ini";
}

void LoadBindings() {
    host_bindings_load_defaults();
    FILE* file = std::fopen(ConfigPath(), "r");
    if (!file) return;
    char line[512];
    while (std::fgets(line, sizeof line, file)) {
        char* value = std::strchr(line, '=');
        if (!value || std::strncmp(line, "bind.", 5) != 0) continue;
        *value++ = 0;
        for (char* end = value - 1; end > line && (end[-1] == ' ' || end[-1] == '\t');) *--end = 0;
        value[std::strcspn(value, "\r\n")] = 0;
        host_bindings_parse(line + 5, value);
    }
    std::fclose(file);
}

void NoteDevice(InputDevice d) { g_device.store(d, std::memory_order_relaxed); }

std::uint8_t StickFromUnit(float v) { return std::uint8_t(std::clamp(int(kStickRest) + int(127 * v), 0, int(kStickHigh))); }

constexpr std::uint64_t kSettleMs = 250;
std::uint64_t g_settle_until = 0;
std::uint32_t g_dpad_blocked = 0;
std::uint32_t g_last_buttons = 0;
bool g_turning_hunter = false;

constexpr float kDragStepPixels = 8, kDragStartPixels = 6;
bool g_dragging_value = false, g_drag_moved = false;
float g_drag_anchor = 0;
EditorDrag::Steps g_drag_steps;
EditorDrag::Rotation g_preview_rotation;
EditorDrag::Zoom g_preview_zoom;

struct MouseRead {
    std::uint32_t held = 0, pressed = 0;
    float wheel = 0, drag_x = 0, drag_y = 0;
};

MouseRead TakeMouse() {
    std::scoped_lock lock(g_mouse_lock);
    MouseRead r{g_mouse.held, g_mouse.pressed, g_mouse.wheel, g_mouse.drag_x, g_mouse.drag_y};
    g_mouse.pressed = 0;
    g_mouse.wheel = g_mouse.drag_x = g_mouse.drag_y = 0;
    return r;
}

void ResetEditorDrag() {
    if (g_dragging_value) MenuPointer::HoldRow(false);
    g_dragging_value = g_drag_moved = false;
    g_drag_steps.Reset();
    MenuPointer::CancelSliderDrag();
    g_preview_rotation.Reset();
    MenuPointer::ClearPreviewRotation();
}

std::uint32_t DragValue(const MouseRead& mouse, bool preview) {
    float x = 0, y = 0;
    const bool on_stage = BbInput::PointerOnStage(x, y);
    if (preview && MenuPointer::NativeSliders()) {
        if (g_dragging_value) MenuPointer::HoldRow(false);
        g_dragging_value = g_drag_moved = false;g_drag_steps.Reset();
        if (!(mouse.held & kLeftButton)) {
            MenuPointer::CancelSliderDrag();
            MenuPointer::ClearPreviewRotation();
        }
        return 0; // The native slider owns absolute pointer-to-value dragging.
    }
    if (preview && (mouse.pressed & kLeftButton) && !g_turning_hunter && on_stage) {
        g_dragging_value = true;
        g_drag_moved = false;
        g_drag_anchor = x;
        g_drag_steps.Reset();
    }
    if (g_turning_hunter) {
        if (g_dragging_value) MenuPointer::HoldRow(false);
        g_dragging_value = g_drag_moved = false;
        g_drag_steps.Reset();
        return 0;
    }
    if (!preview || !(mouse.held & kLeftButton)) {
        ResetEditorDrag();
        return 0;
    }
    if (g_dragging_value && on_stage) {
        if (!g_drag_moved && std::fabs(x - g_drag_anchor) >= kDragStartPixels) {
            g_drag_moved = true;
            MenuPointer::HoldRow(true);
        }
        if (g_drag_moved) {
            const int steps = int((x - g_drag_anchor) / kDragStepPixels);
            g_drag_steps.Push(steps);
            g_drag_anchor += float(steps) * kDragStepPixels;
        }
    }
    const int direction = g_drag_steps.Poll(SDL_GetTicks());
    static unsigned reports = 0;
    if (direction && reports < 4) {
        ++reports;
        host_log("character creation: mouse slider output %s", direction > 0 ? "right" : "left");
    }
    return direction > 0 ? kRight : direction < 0 ? kLeft : 0;
}

void AddKeyboardAndMouse(PadState& pad, const bool* keys, int nkeys, const MouseRead& mouse, bool menu,
                         bool keys_move_hunter, bool camera) {
    const auto& s = BbSettings::Get();
    const bool preview = BbEngine::ChangeAppearance::PreviewShown();
    if (!preview || !s.mouse_menu) { ResetEditorDrag(); g_preview_zoom.Reset(); MenuPointer::ClearPreviewZoom(); g_turning_hunter = false; }
    bool held[kBindCount] = {};
    if (keys) host_bindings_keys_held(keys, nkeys, held);
    const unsigned mouse_inputs = mouse.held | mouse.pressed |
        (mouse.wheel > 0 ? kWheelUpInput : mouse.wheel < 0 ? kWheelDownInput : 0);
    bool mouse_held[kBindCount]{};
    host_bindings_mouse_held(mouse_inputs, mouse_held);
    if (!menu) for (int a = 0; a < kBindCount; ++a) held[a] |= mouse_held[a];
    bool suppressed[kBindCount]{};
    Chords::Resolve(keys, nkeys, menu ? 0 : mouse_inputs, held, suppressed);
    host_bindings_resolve_movement(held);
    if (!menu) { held[kBindConfirm] = false; held[kBindBack] = false; }
    if (!menu && held[kBindPreviousItem] && !g_previous_item_was) native_item_cycle_request(true);
    if (menu) native_item_cycle_cancel(); else menu_pointer_scroll(0);
    g_previous_item_was = !menu && held[kBindPreviousItem];
    if (held[kBindPreviousItem]) held[kBindSwitchItem] = false;
    const std::uint64_t now = SDL_GetTicks();
    const bool navigating = menu && (preview || MenuPointer::Navigating());
    // Character creation and the mirror own the keyboard, even with a world player loaded.
    const bool world_movement = !preview &&
        BbInputPolicy::WorldMovement(s.mouse_menu, MenuPointer::FrontEnd(), navigating);
    std::uint32_t dpad = 0;
    if (navigating) {
        const auto down = [&](int sc) { return keys && sc >= 0 && sc < nkeys && keys[sc]; };
        std::uint32_t arrows = 0;
        if (down(SDL_SCANCODE_UP)) arrows |= kUp;
        if (down(SDL_SCANCODE_DOWN)) arrows |= kDown;
        if (down(SDL_SCANCODE_LEFT)) arrows |= kLeft;
        if (down(SDL_SCANCODE_RIGHT)) arrows |= kRight;
        dpad = BbInputPolicy::MenuKeyboard(held, world_movement, arrows);
        g_dpad_blocked &= dpad;
        if (now >= g_settle_until) pad.buttons |= dpad & ~g_dpad_blocked;
        if (down(SDL_SCANCODE_RETURN)) pad.buttons |= menu_confirm_button();
        if (down(SDL_SCANCODE_BACKSPACE)) pad.buttons |= menu_back_button();
    }
    g_keyboard_moving = world_movement &&
        (held[kBindMoveF] || held[kBindMoveB] || held[kBindMoveL] || held[kBindMoveR]);
    host_bindings_apply(held, held[kBindStrongMod], pad);
    {
        std::scoped_lock lock(g_mouse_lock);
        if (menu) g_weapon_wheel.Reset();
        else pad.buttons |= g_weapon_wheel.Poll(std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count());
    }

    if (!preview || !(mouse.held & kLeftButton)) g_turning_hunter = false;
    if (preview && (mouse.pressed & kLeftButton)) {
        float x, y;
        g_turning_hunter = BbInput::PointerOnStage(x, y) && x >= kPreviewLeft && x <= kPreviewRight &&
                           y >= kPreviewTop && y <= kPreviewBottom;
    }

    if (menu) {
        if (s.mouse_menu) {
            if ((mouse.pressed & kLeftButton) && !g_turning_hunter) MenuPointer::Click();
            // Editor navigation is independent of gameplay bindings (DS3 binds RMB to L2).
            if (mouse.pressed & kRightButton) pad.buttons |= menu_back_button();
            if (preview) {
                if (mouse.pressed & (1u << 1)) MenuPointer::ResetPreview();
                // Apply all wheel distance at the native preview update. Preserve
                // the trigger fallback only if that version-checked hook is absent.
                if (!MenuPointer::ZoomPreview(mouse.wheel)) {
                    const int zoom = g_preview_zoom.Poll(mouse.wheel, now);
                    if (zoom > 0) { pad.buttons |= kL2; pad.l2 = 255; }
                    if (zoom < 0) { pad.buttons |= kR2; pad.r2 = 255; }
                }
            } else if (navigating || keys_move_hunter) {
                if (mouse.wheel) menu_pointer_scroll(mouse.wheel > 0 ? -1 : 1);
            }
            pad.buttons |= MenuPointer::TakePress();
            pad.buttons |= DragValue(mouse, preview);
        }

        const std::uint32_t closing = menu_confirm_button() | menu_back_button() | kOptions;
        if (pad.buttons & ~g_last_buttons & closing) {
            g_dpad_blocked |= dpad;
            g_settle_until = now + kSettleMs;
        }
    }

    if (g_turning_hunter) {
        const float gain = 2.5f * float(std::max(1, int(s.mouse_sensitivity)));
        if (!MenuPointer::RotatePreview(mouse.drag_x, mouse.drag_y, gain)) {
            // Retain the old stick adapter only when native signatures differ.
            const auto axes = g_preview_rotation.Poll(mouse.drag_x, mouse.drag_y, now, gain);
            pad.rx = axes.x;
            pad.ry = axes.y;
        }
        static unsigned reports = 0;
        if ((mouse.drag_x != 0 || mouse.drag_y != 0) && reports < 4) {
            ++reports;
            host_log("character creation: preview drag dx=%.2f dy=%.2f", double(mouse.drag_x), double(mouse.drag_y));
        }
    }
    float fx, fy;
    if (camera && MouseCamera::Flick(fx, fy)) {
        pad.rx = StickFromUnit(fx);
        pad.ry = StickFromUnit(fy);
    }
    g_last_buttons = pad.buttons;
}
}

void host_log(const char* format, ...) {
    std::fputs("Input: ", stdout);
    va_list args;
    va_start(args, format);
    std::vprintf(format, args);
    va_end(args);
    std::putchar('\n');
}

InputDevice host_input_device() { return g_device.load(std::memory_order_relaxed); }

bool debug_menu_active() { return false; }

namespace BbInput {
bool Enabled() { return g_active.load(std::memory_order_relaxed) && BbSettings::Get().pc_controls; }

bool Install(std::uint8_t* image, std::size_t size) {
    if (!BbSettings::Get().pc_controls) return false;
    LoadBindings();
    BbInputConfig::append_bindings = [](BbInputConfig::Items& lines) {
        for (int a = 0; a < kBindDebugMenu; ++a)
            lines.emplace_back(std::string("bind.") + host_binding_info(a).key, host_binding_ini_value(a));
        for (int a = 0; a < 19; ++a) {
            char name[64], value[256];
            if (runtime_pad_export_binding(a, name, sizeof name, value, sizeof value))
                lines.emplace_back(std::string("pad.") + name, value);
        }
    };
    g_saved_serial = host_bindings_serial();
    const bool camera = MouseCamera::Install(image, size);
    MenuPointer::Install(image, size);
    BbEngine::ItemCycle::Install(image, size);
    g_active.store(true, std::memory_order_relaxed);
    host_log("keyboard and mouse play on, the mouse camera %s", camera ? "ready" : "unavailable");
    return true;
}

void Tick() {
    if (!g_active.load(std::memory_order_relaxed)) return;
    const std::uint64_t serial = host_bindings_serial();
    if (serial != g_saved_serial || runtime_pad_bindings_serial() != g_saved_pad_serial) {
        BbSettings::Save();
        g_saved_serial = serial;
        g_saved_pad_serial = runtime_pad_bindings_serial();
    }
}

void Event(const SDL_Event& e, bool blocked) {
    if (!g_active.load(std::memory_order_relaxed)) return;
    switch (e.type) {
    case SDL_EVENT_WINDOW_FOCUS_LOST: {
        host_bindings_reset_key_events();
        g_keyboard_moving = false;
        g_event_keys.fill(false);
        g_weapon_wheel.Reset();
        g_focused = false;
        g_wants_relative = false;
        MouseCamera::Reset();
        std::scoped_lock lock(g_mouse_lock);
        g_mouse.held = g_mouse.pressed = 0;
        g_mouse.wheel = 0;
        return;
    }
    case SDL_EVENT_WINDOW_FOCUS_GAINED: g_focused = true; break;
    case SDL_EVENT_WINDOW_MOUSE_LEAVE: g_in_window = false; break;
    case SDL_EVENT_WINDOW_MOUSE_ENTER: g_in_window = true; break;
    case SDL_EVENT_GAMEPAD_BUTTON_DOWN: if (!BbSettings::Get().keyboard_mouse) NoteDevice(InputDevice::Pad); break;
    case SDL_EVENT_GAMEPAD_AXIS_MOTION:
        if (!BbSettings::Get().keyboard_mouse && std::abs(e.gaxis.value) > 8000) NoteDevice(InputDevice::Pad);
        break;
    default: break;
    }
    if (NativePages::ControllerCapturing()) {
        if (e.type == SDL_EVENT_KEY_DOWN && e.key.scancode == SDL_SCANCODE_ESCAPE) NativePages::CancelControllerCapture();
        if (e.type == SDL_EVENT_GAMEPAD_BUTTON_DOWN) NativePages::ControllerEvent(e.gbutton.button);
        if (e.type == SDL_EVENT_GAMEPAD_AXIS_MOTION && e.gaxis.value > 16000) {
            if (e.gaxis.axis == SDL_GAMEPAD_AXIS_LEFT_TRIGGER) NativePages::ControllerEvent(SDL_GAMEPAD_BUTTON_COUNT);
            if (e.gaxis.axis == SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) NativePages::ControllerEvent(SDL_GAMEPAD_BUTTON_COUNT + 1);
        }
        return;
    }
    if (e.type == SDL_EVENT_KEY_DOWN || e.type == SDL_EVENT_KEY_UP) {
        host_bindings_key_event(e.key.scancode, e.type == SDL_EVENT_KEY_DOWN);
        if (e.key.scancode >= 0 && e.key.scancode < int(g_event_keys.size()))
            g_event_keys[e.key.scancode] = e.type == SDL_EVENT_KEY_DOWN;
    }
    if (blocked) return;
    switch (e.type) {
    case SDL_EVENT_KEY_DOWN:
        NoteDevice(InputDevice::KeyboardMouse);
        host_bind_capture_key(e.key.scancode, e.key.mod);
        if (e.key.scancode == SDL_SCANCODE_DELETE) host_bind_note_clear_key();
        break;
    case SDL_EVENT_KEY_UP: host_bind_capture_key_up(e.key.scancode); break;
    case SDL_EVENT_MOUSE_MOTION: {
        NoteDevice(InputDevice::KeyboardMouse);
        std::scoped_lock lock(g_mouse_lock);
        g_mouse.x = e.motion.x;
        g_mouse.y = e.motion.y;
        if (g_relative && g_focused) {
            MouseCamera::AddCounts(e.motion.xrel, e.motion.yrel);
        } else if (g_mouse.held & kLeftButton) {
            g_mouse.drag_x += e.motion.xrel;
            g_mouse.drag_y += e.motion.yrel;
        }
        break;
    }
    case SDL_EVENT_MOUSE_BUTTON_DOWN:
    case SDL_EVENT_MOUSE_BUTTON_UP: {
        NoteDevice(InputDevice::KeyboardMouse);
        const int button = e.button.button;
        const std::uint32_t bit = button >= 1 && button <= 5 ? 1u << (button - 1) : 0;
        const bool down = e.type == SDL_EVENT_MOUSE_BUTTON_DOWN;
        const bool rebound = down && host_bind_capture_mouse(button);
        std::scoped_lock lock(g_mouse_lock);
        g_mouse.x = e.button.x;
        g_mouse.y = e.button.y;
        if (down) {
            g_mouse.held |= bit;
            if (!rebound) g_mouse.pressed |= bit;
        } else {
            g_mouse.held &= ~bit;
        }
        break;
    }
    case SDL_EVENT_MOUSE_WHEEL:
        NoteDevice(InputDevice::KeyboardMouse);
        if (e.wheel.y != 0 && !host_bind_capture_mouse(e.wheel.y > 0 ? 6 : 7)) {
            std::scoped_lock lock(g_mouse_lock);
            const unsigned direction = e.wheel.y > 0 ? kWheelUpInput : kWheelDownInput;
            std::uint32_t weapons = 0;
            if (!BbEngine::ChangeAppearance::PreviewShown() && !menu_pointer_in_menu()) for (const int action : {kBindSwitchRight, kBindSwitchLeft}) {
                const auto chord = Chords::Get(action);
                const bool match = chord.active()
                    ? (chord.mouse & direction) && Chords::Matches(chord, g_event_keys.data(), int(g_event_keys.size()), g_mouse.held | direction)
                    : host_binding_mouse(action) == (direction == kWheelUpInput ? 6 : 7);
                if (match) weapons |= host_binding_info(action).button;
            }
            if (weapons) g_weapon_wheel.Push(weapons, std::max(1, int(std::abs(e.wheel.y))));
            else g_mouse.wheel += e.wheel.y;
        }
        break;
    default: break;
    }
}

void Pump(SDL_Window* window, bool text_active) {
    g_text_entry = text_active;
    if (!g_active.load(std::memory_order_relaxed)) return;

    if (!g_focused && (SDL_GetWindowFlags(window) & SDL_WINDOW_INPUT_FOCUS)) g_focused = true;
    int w = 0, h = 0;
    SDL_GetWindowSize(window, &w, &h);
    {
        std::scoped_lock lock(g_mouse_lock);
        g_mouse.window_w = w;
        g_mouse.window_h = h;
    }
    const bool want = g_wants_relative && BbSettings::Get().pc_controls && !text_active && !BbOverlay::CapturesInput() &&
                      g_focused;
    if (want != g_relative && SDL_SetWindowRelativeMouseMode(window, want)) {
        g_relative = want;
        MouseCamera::Reset();
        if (want) SDL_HideCursor();
        else SDL_ShowCursor();
    }
}

bool MouseIsMenuPointer() {
    return BbSettings::Get().mouse_menu && !BbOverlay::MenuOpen() && !g_relative &&
           g_device.load(std::memory_order_relaxed) == InputDevice::KeyboardMouse;
}

bool PointerLeftHeld() {
    std::scoped_lock lock(g_mouse_lock);
    return g_active.load(std::memory_order_relaxed) && g_focused && g_in_window &&
        !g_text_entry && (g_mouse.held & kLeftButton) != 0;
}

bool PointerOnStage(float& x, float& y) {
    std::scoped_lock lock(g_mouse_lock);
    if (!g_in_window || !g_focused || g_relative || g_mouse.window_w <= 0 || g_mouse.window_h <= 0) return false;

    const auto& s = BbSettings::Get();
    const int output = std::clamp(BbSettings::ResolutionNeedsRestart() ? s.startup_output_res : s.output_res.load(), 0,
                                  BbSettings::OutputCount - 1);
    const float ow = float(BbSettings::OutputWidth(output)), oh = float(BbSettings::OutputHeight(output));
    const float ww = float(g_mouse.window_w), wh = float(g_mouse.window_h);
    const float fit = std::min(ww / ow, wh / oh);
    const float output_x = (ww - ow * fit) / 2, output_y = (wh - oh * fit) / 2;
    if (!s.widescreen) {
        x = (g_mouse.x - output_x) / fit * 1920 / ow;
        y = (g_mouse.y - output_y) / fit * 1080 / oh;
        return true;
    }
    const float stage = std::min(ow / 1920, oh / 1080);
    const float stage_x = output_x + (ow - 1920 * stage) * fit / 2, stage_y = output_y + (oh - 1080 * stage) * fit / 2;
    x = (g_mouse.x - stage_x) / (fit * stage);
    y = (g_mouse.y - stage_y) / (fit * stage);
    return true;
}

bool KeyboardMoving() { return g_keyboard_moving.load(std::memory_order_relaxed); }

bool TextEntryOpen() { return g_text_entry.load(std::memory_order_relaxed); }

PadState LastPad() {
    std::scoped_lock lock(g_pad_lock);
    return g_last_pad;
}
}

extern "C" int bbgpu_menu_buttons(std::uint32_t* confirm, std::uint32_t* back) {
    if (!g_active.load(std::memory_order_relaxed)) return 0;
    *confirm = menu_confirm_button();
    *back = menu_back_button();
    return MenuPointer::Navigating() ? 1 : 0;
}

extern "C" int bbgpu_pc_input(BbPcPad* d, const bool* keys, int nkeys, int gamepad_present) {
    const auto& s = BbSettings::Get();
    if (!g_active.load(std::memory_order_relaxed) || !s.pc_controls) {
        g_wants_relative = false;
        g_keyboard_moving = false;
        ResetEditorDrag(); g_preview_zoom.Reset(); MenuPointer::ClearPreviewZoom(); g_turning_hunter = false;
        MenuPointer::CancelClicks();
        return 0;
    }
    PadState pad;
    const bool allow_pad = gamepad_present && !s.keyboard_mouse;
    if (allow_pad) {
        pad.buttons = d->buttons; pad.l2 = d->l2; pad.r2 = d->r2;
        pad.lx = d->lx; pad.ly = d->ly; pad.rx = d->rx; pad.ry = d->ry;
        pad.touch_count = std::min<std::uint8_t>(d->touch_count, 2);
        for (int i = 0; i < pad.touch_count; ++i) pad.touch[i] = {d->touch[i].x, d->touch[i].y, d->touch[i].id};
    } else NoteDevice(InputDevice::KeyboardMouse);
    const bool keyboard_mouse = g_device.load(std::memory_order_relaxed) == InputDevice::KeyboardMouse;
    // The editor remains active between focused list updates and while rotating its preview.
    const bool preview = BbEngine::ChangeAppearance::PreviewShown();
    const bool menu = preview || menu_pointer_in_menu();
    const bool keys_move_hunter = menu && !preview && s.mouse_menu && !MenuPointer::FrontEnd();
    const bool stick_camera = allow_pad && (std::abs(int(d->rx) - 128) > 32 || std::abs(int(d->ry) - 128) > 32);
    const bool camera = s.keyboard_mouse && s.mouse_camera && MouseCamera::Installed() && !menu &&
                        !g_text_entry && g_focused && keyboard_mouse && !stick_camera;
    g_wants_relative = camera;
    MouseCamera::Publish(s.mouse_sensitivity, s.mouse_invert_x, s.mouse_invert_y,
                         camera && !s.mouse_auto_rotation, camera);

    const MouseRead mouse = TakeMouse();
    bool any_down = mouse.held != 0;
    for (int i = 0; keys && i < nkeys && !any_down; ++i) any_down = keys[i];
    host_bind_capture_note_held(any_down);
    if (!g_focused || g_text_entry || BbOverlay::CapturesInput() || host_bind_capture_blocking() || NativePages::ControllerCapturing()) {

        pad = PadState{};
        ResetEditorDrag(); g_preview_zoom.Reset(); MenuPointer::ClearPreviewZoom(); g_turning_hunter = false;
        MenuPointer::CancelClicks();
        g_keyboard_moving = false;
        { std::scoped_lock lock(g_mouse_lock); g_weapon_wheel.Reset(); }
        g_wants_relative = false;
    } else {
        AddKeyboardAndMouse(pad, keys, nkeys, mouse, menu, keys_move_hunter, camera);
    }
    {
        std::scoped_lock lock(g_pad_lock);
        g_last_pad = pad;
    }
    d->buttons = pad.buttons;
    d->lx = pad.lx;
    d->ly = pad.ly;
    d->rx = pad.rx;
    d->ry = pad.ry;
    d->l2 = pad.l2;
    d->r2 = pad.r2;
    d->touch_count = pad.touch_count;
    for (int i = 0; i < pad.touch_count; ++i) {
        d->touch[i].x = pad.touch[i].x;
        d->touch[i].y = pad.touch[i].y;
        d->touch[i].id = pad.touch[i].id;
    }
    return 1;
}

// Compatibility for the custom native binding pages; the active input implementation is shared.
std::uint32_t input_delivered_buttons() { return BbInput::LastPad().buttons; }
bool host_keyboard_moving() { return BbInput::KeyboardMoving(); }
