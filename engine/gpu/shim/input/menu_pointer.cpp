// SPDX-License-Identifier: GPL-3.0-or-later
#include "menu_pointer.h"
#include "input.h"
#include "menu_keyboard_policy.h"
#include "pad.h"
#include "editor_drag.h"
#include <mutex>
#include "../bbport_settings.h"
namespace BbOverlay { bool MenuOpen(); bool CapturesInput(); }
#include "../engine/engine_hooks.h"
#include "../engine/engine_state.h"
#include "../engine/option_menu.h"
#include "../engine/change_appearance.h"
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <iterator>

namespace MenuPointer {
namespace {
using BbEngine::Call;

constexpr std::size_t kListUpdate = 0x1acff30;

constexpr std::uint8_t kListUpdateEntry[] = {0x55, 0x48, 0x89, 0xe5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55,
                                             0x41, 0x54, 0x53, 0x48, 0x81, 0xec, 0x98, 0x00, 0x00, 0x00};
constexpr std::size_t kSetCursor = 0x1acf6b0, kScrollBy = 0x1ad0dd0, kHideCursors = 0x1acf0e0;
constexpr std::size_t kChildClip = 0x1adb220, kCursorClipName = 0x4934044;
constexpr std::size_t kStageMatrix = 0x2040;
constexpr std::size_t kCodeEnd = 0x50d96dc;

constexpr std::size_t kListener = 0x30, kItems = 0x70, kCursor = 0x74, kCursorOverride = 0x78;
constexpr std::size_t kCells = 0x7c, kLines = 0x80, kAlong = 0x84, kScrollLines = 0x24c;
constexpr std::size_t kRootType = 0xc8, kRootData = 0xd0, kProvider = 0x258, kWraps = 0x2b0;

constexpr std::size_t kValue = 0x30, kValueInterface = 0x40, kValueType = 0x48, kValueData = 0x50;
constexpr std::size_t kDisplayObject = 0x88, kParent = 0x38, kFlagsHigh = 0x6b, kBoundsSlot = 0x1f0;
constexpr std::uint8_t kVisible = 0x40;

constexpr std::size_t kMenuFlow = 0x5580e38, kFlowBlocked = 0xa78, kFlowMenu = 0xa88, kFlowStarted = 0xb11;
constexpr std::size_t kInputBlocker = 0x5562878, kBlockerBusy = 0x2e4;

constexpr std::size_t kRegion = 0x5128948;
constexpr int kRegionCircleDecides = 5;

constexpr std::size_t kMenuSound = 0x557fc60, kMenuSoundLabel = 0x557fc68, kCursorMoveLabel = 0x49344e4;

std::uint8_t* g_image = nullptr;
bool g_log = false;

std::atomic<std::int64_t> g_focus_ms{-100000};
std::atomic<std::int64_t> g_choice_ms{-100000};
std::atomic<int> g_scroll{0};
std::atomic<bool> g_holding_row{false};
std::atomic<bool> g_click{false};
std::atomic<std::int64_t> g_click_ms{0};
std::atomic<std::uint32_t> g_press{0};
std::mutex g_editor_click_lock;
EditorDrag::Clicks g_editor_clicks;
EditorDrag::WheelZoom g_preview_wheel;
bool g_preview_zoom_hook = false, g_preview_rotation_hook = false;
std::mutex g_preview_drag_lock;
EditorDrag::DragRotation g_preview_drag;
constexpr std::int64_t kClickMs = 250, kInMenuMs = 34, kNavigatingMs = 70;

std::int64_t NowMs() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch())
        .count();
}

std::uint64_t Image(std::size_t offset) { return reinterpret_cast<std::uint64_t>(g_image) + offset; }

template <typename T> bool Read(std::uint64_t at, T& out) {
    return at >= 0x10000 && BbEngine::host_read_safe(reinterpret_cast<const void*>(at), &out, sizeof out);
}
template <typename T> T Field(const std::uint8_t* object, std::size_t offset) {
    T v;
    std::memcpy(&v, object + offset, sizeof v);
    return v;
}

bool GameFunction(std::uint64_t at) { return g_image && at >= Image(0) && at < Image(kCodeEnd); }
std::uint64_t VirtualFunction(std::uint64_t object, std::size_t slot) {
    std::uint64_t vtable = 0, fn = 0;
    return object && Read(object, vtable) && Read(vtable + slot, fn) && GameFunction(fn) ? fn : 0;
}

class Wrapped {
public:
    Wrapped() = default;
    Wrapped(const Wrapped&) = delete;
    Wrapped& operator=(const Wrapped&) = delete;
    ~Wrapped() {
        if (!(Field<std::uint32_t>(bytes_, kValueType) & 0x40)) return;
        const auto interface = Field<std::uint64_t>(bytes_, kValueInterface);
        if (const std::uint64_t release = VirtualFunction(interface, 0x18))
            Call(release, interface, bytes_ + kValue, Field<std::uint64_t>(bytes_, kValueData));
    }
    std::uint8_t* Bytes() { return bytes_; }

    std::uint64_t Object() const {
        std::uint64_t object = 0;
        const auto data = Field<std::uint64_t>(bytes_, kValueData);
        return (Field<std::uint32_t>(bytes_, kValueType) & 0x8f) == 10 && Read(data + kDisplayObject, object) ? object : 0;
    }

private:
    alignas(16) std::uint8_t bytes_[0x80]{};
};

bool ItemAt(std::uint8_t* list, std::uint64_t cell, Wrapped& out) {
    const auto provider = Field<std::uint64_t>(list, kProvider);
    const std::uint64_t make = VirtualFunction(provider, 0x18);
    if (!make) return false;
    Call(make, out.Bytes(), provider, &cell);
    return out.Object() != 0;
}
void Child(Wrapped& parent, const char* path, Wrapped& out) { Call(Image(kChildClip), out.Bytes(), parent.Bytes(), path); }
void CursorClip(Wrapped& item, Wrapped& out) {
    Call(Image(kChildClip), out.Bytes(), item.Bytes(), Image(kCursorClipName));
}

bool Shown(std::uint64_t object) {
    for (int depth = 0; object && depth < 32; ++depth) {
        std::uint8_t flags = 0;
        if (!Read(object + kFlagsHigh, flags) || !(flags & kVisible) || !Read(object + kParent, object)) return false;
    }
    return true;
}

struct Rect {
    float left = 0, top = 0, right = 0, bottom = 0;
    float CentreX() const { return (left + right) / 2; }
    float CentreY() const { return (top + bottom) / 2; }
};

bool Contains(std::uint64_t object, float x, float y, float grow, Rect* on_stage) {
    const std::uint64_t bounds_fn = VirtualFunction(object, kBoundsSlot);
    if (!bounds_fn) return false;
    alignas(16) float identity[8] = {1, 0, 0, 0, 0, 1, 0, 0};
    alignas(16) float b[4] = {};
    Call(bounds_fn, b, object, identity);
    if (!(b[2] > b[0] && b[3] > b[1])) return false;
    alignas(16) float m[8] = {1, 0, 0, 0, 0, 1, 0, 0};
    Call(Image(kStageMatrix), object, m);
    if (on_stage) {

        Rect r{1e9f, 1e9f, -1e9f, -1e9f};
        for (const float bx : {b[0], b[2]}) {
            for (const float by : {b[1], b[3]}) {
                const float sx = (m[0] * bx + m[1] * by + m[3]) / 20, sy = (m[4] * bx + m[5] * by + m[7]) / 20;
                r = {std::min(r.left, sx), std::min(r.top, sy), std::max(r.right, sx), std::max(r.bottom, sy)};
            }
        }
        *on_stage = r;
    }
    const float det = m[0] * m[5] - m[1] * m[4];
    if (std::fabs(det) < 1e-12f) return false;

    const float px = x * 20 - m[3], py = y * 20 - m[7];
    const float lx = (m[5] * px - m[1] * py) / det, ly = (m[0] * py - m[4] * px) / det;
    const float g = grow * 20 / std::sqrt(std::fabs(det));
    return lx >= b[0] - g && lx <= b[2] + g && ly >= b[1] - g && ly <= b[3] + g;
}

int CursorOf(const std::uint8_t* list) {
    const auto over = Field<std::int32_t>(list, kCursorOverride);
    return over >= 0 ? over : Field<std::int32_t>(list, kCursor);
}

struct Pointed {
    int index = -1;
    std::uint64_t cell = 0;
};

Pointed ItemUnder(std::uint8_t* list, float x, float y, bool log) {
    const int items = Field<std::int32_t>(list, kItems), cells = Field<std::int32_t>(list, kCells);
    const int lines = Field<std::int32_t>(list, kLines);
    if (items <= 0 || cells <= 0 || lines <= 0 || cells > 64 || lines > 64) return {};
    const int along = Field<std::int32_t>(list, kAlong), scrolled = Field<std::int32_t>(list, kScrollLines);
    const bool wraps = Field<std::uint8_t>(list, kWraps) != 0;

    const int line_length = lines == 1 ? std::max({cells, items, 1}) : cells;
    Pointed best;
    float best_distance = 0;
    for (int line = 0; line < lines; ++line) {
        for (int cell = 0; cell < cells; ++cell) {
            int index = (scrolled + line) * line_length + along + cell;
            if (wraps) index %= items;
            if (index < 0 || index >= items) continue;
            const std::uint64_t packed = std::uint32_t(cell) | std::uint64_t(std::uint32_t(line)) << 32;
            Wrapped item;
            if (!ItemAt(list, packed, item) || !Shown(item.Object())) continue;
            Wrapped highlight;
            CursorClip(item, highlight);
            const std::uint64_t target = highlight.Object() ? highlight.Object() : item.Object();
            Rect where;
            bool on = Contains(target, x, y, 0, &where);
            for (const char* widget : {"Widgets/ComboBox", "Widgets/Slider", "Slider"}) {
                if (on) break;
                Wrapped part;
                Child(item, widget, part);
                Rect widget_where;
                if (part.Object() && Shown(part.Object()) && Contains(part.Object(), x, y, 0, &widget_where)) {
                    on = true;
                    where = widget_where;
                }
            }
            if (!on && BbEngine::ChangeAppearance::PreviewShown()) {
                // Creator rows can have a narrow/empty Cursor and arrows outside
                // the slider bar. Hit the visible row and its actual arrow clips.
                for (const char* widget : {"Widgets/Slider/LeftArrow", "Widgets/Slider/RightArrow",
                                          "Slider/LeftArrow", "Slider/RightArrow", "LeftArrow", "RightArrow"}) {
                    Wrapped part;
                    Child(item, widget, part);
                    Rect widget_where;
                    if (part.Object() && Shown(part.Object()) && Contains(part.Object(), x, y, 4, &widget_where)) {
                        on = true; where = widget_where; break;
                    }
                }
                if (!on && Contains(item.Object(), x, y, 0, &where)) on = true;
            }
            if (log)
                host_log("pointer:   item %d (cell %d, line %d) %s at %.0f,%.0f-%.0f,%.0f%s", index, cell, line,
                         target == item.Object() ? "itself" : "its Cursor", where.left, where.top, where.right,
                         where.bottom, on ? "  <- pointer" : "");
            if (!on) continue;
            const float dx = where.CentreX() - x, dy = where.CentreY() - y, distance = dx * dx + dy * dy;
            if (best.index < 0 || distance < best_distance) {
                best = {index, packed};
                best_distance = distance;
            }
        }
    }
    return best;
}

std::uint32_t ClickMeaning(std::uint8_t* list, const Pointed& p, float x, float y) {
    const std::uint32_t decide = menu_confirm_button();
    if (BbEngine::Options::ClickDecides(list, p.index)) return decide;
    Wrapped item;
    if (!ItemAt(list, p.cell, item)) return decide;
    const auto on = [&](const char* path, float grow, Rect* where) {
        Wrapped part;
        Child(item, path, part);
        const bool shown = part.Object() && Shown(part.Object());
        const bool hit = shown && Contains(part.Object(), x, y, grow, where);
        if (g_log && part.Object())
            host_log("pointer:   %s %s at %.0f,%.0f-%.0f,%.0f%s", path, shown ? "shown" : "hidden", where->left,
                     where->top, where->right, where->bottom, hit ? "  <- click" : "");
        return hit;
    };
    Rect where;
    if (BbEngine::ChangeAppearance::PreviewShown()) {
        // Some creator pages bind Slider directly rather than under Widgets.
        // Small hit padding prevents adjacent arrows from sharing a hit region.
        for (const char* path : {"Widgets/Slider/LeftArrow", "Slider/LeftArrow", "LeftArrow"})
            if (on(path, 4, &where)) return PadBits::kLeft;
        for (const char* path : {"Widgets/Slider/RightArrow", "Slider/RightArrow", "RightArrow"})
            if (on(path, 4, &where)) return PadBits::kRight;
    } else {
        if (on("Widgets/Slider/LeftArrow", 14, &where)) return PadBits::kLeft;
        if (on("Widgets/Slider/RightArrow", 14, &where)) return PadBits::kRight;
    }
    if (on("Widgets/ComboBox", 0, &where)) return x < where.CentreX() ? PadBits::kLeft : PadBits::kRight;
    if (on("Slider", 0, &where)) return x < where.CentreX() ? PadBits::kLeft : PadBits::kRight;
    return decide;
}

void ShowCursorOf(std::uint8_t* list, std::uint64_t cell) {
    Wrapped item;
    if (!ItemAt(list, cell, item)) return;
    Wrapped highlight;
    CursorClip(item, highlight);
    if (!highlight.Object()) return;
    if (const std::uint64_t set_visible = VirtualFunction(reinterpret_cast<std::uint64_t>(highlight.Bytes()), 8))
        Call(set_visible, highlight.Bytes(), 1);
}

void CursorMoved(std::uint8_t* list, int index) {
    const auto listener = Field<std::uint64_t>(list, kListener);
    if (const std::uint64_t told = VirtualFunction(listener, 0x10)) {
        std::int32_t at = index;
        Call(told, listener, &at);
    }
    auto* sound = reinterpret_cast<std::int32_t*>(Image(kMenuSound));
    if (sound[1] <= 0) {
        sound[0] = 0;
        sound[1] = 0;
        const std::uint64_t label = Image(kCursorMoveLabel);
        std::memcpy(reinterpret_cast<void*>(Image(kMenuSoundLabel)), &label, sizeof label);
    }
}

std::uint64_t Flow() {
    std::uint64_t flow = 0;
    return g_image && Read(Image(kMenuFlow), flow) ? flow : 0;
}

bool MenuOpen() {
    std::uint64_t menu = 0;
    const std::uint64_t flow = Flow();
    return flow && Read(flow + kFlowMenu, menu) && menu;
}

bool InputBlocked() {
    std::uint64_t blocked = 0, blocker = 0;
    std::uint32_t busy = 0;
    const std::uint64_t flow = Flow();
    if (flow && Read(flow + kFlowBlocked, blocked) && blocked) return true;
    return g_image && Read(Image(kInputBlocker), blocker) && blocker && Read(blocker + kBlockerBusy, busy) && busy;
}

bool RegionDecidesWithCircle() {
    std::int32_t region = -1;
    return g_image && Read(Image(kRegion), region) && region == kRegionCircleDecides;
}

bool InMenu() {
    return BbEngine::ChangeAppearance::PreviewShown() || FrontEnd() || MenuOpen() || NowMs() - g_focus_ms.load(std::memory_order_relaxed) < kInMenuMs;
}

bool DirectionHeld() {
    const PadState pad = BbInput::LastPad();
    if (pad.buttons & PadBits::kDpad) return true;
    if (!BbEngine::ChangeAppearance::PreviewShown() &&
        BbInputPolicy::FilterList(BbSettings::Get().mouse_menu, FrontEnd(),
            BbInput::KeyboardMoving(), host_input_device() == InputDevice::Pad)) return false;
    if (BbSettings::Get().menu_move && !FrontEnd()) return false;
    const auto pushed = [](std::uint8_t v) { return v < 64 || v > 192; };
    return pushed(pad.lx) || pushed(pad.ly);
}

struct Seen {
    const std::uint8_t* list = nullptr;
    float x = 0, y = 0;
    std::int64_t ms = 0;
    bool described = false;
};
Seen g_seen[32];
unsigned g_seen_next = 0;
constexpr std::int64_t kSeenMs = 250;

Seen& SeenBy(const std::uint8_t* list, bool& fresh) {
    const std::int64_t now = NowMs();
    for (Seen& s : g_seen) {
        if (s.list != list) continue;
        fresh = now - s.ms > kSeenMs;
        if (fresh) s.described = false;
        s.ms = now;
        return s;
    }
    Seen& s = g_seen[g_seen_next++ % std::size(g_seen)];
    s = {list, 0, 0, now, false};
    fresh = true;
    return s;
}

bool ClickWaiting() {
    if (!g_click.load(std::memory_order_relaxed)) return false;
    if (NowMs() - g_click_ms.load(std::memory_order_relaxed) <= kClickMs) return true;
    g_click.store(false, std::memory_order_relaxed);
    return false;
}

void SpinnerClick(std::uint8_t* list, float x, float y) {
    std::uint64_t root = 0;
    const auto data = Field<std::uint64_t>(list, kRootData);
    if ((Field<std::uint32_t>(list, kRootType) & 0x8f) != 10 || !Read(data + kDisplayObject, root) || !root) return;
    Rect where;
    const bool on = Shown(root) && Contains(root, x, y, 0, &where);
    if (g_log)
        host_log("pointer: spinner %p at %.0f,%.0f-%.0f,%.0f%s", static_cast<void*>(list), where.left, where.top,
                 where.right, where.bottom, on ? "  <- click" : "");
    if (on && g_click.exchange(false))
        g_press.store(x < where.CentreX() ? PadBits::kLeft : PadBits::kRight, std::memory_order_relaxed);
}

bool OnScreen(const std::uint8_t* list) {
    std::uint64_t root = 0;
    const auto data = Field<std::uint64_t>(list, kRootData);
    if ((Field<std::uint32_t>(list, kRootType) & 0x8f) != 10 || !Read(data + kDisplayObject, root) || !root) return true;
    return Shown(root);
}

std::atomic<const std::uint8_t*> g_choice_list{nullptr};

void PointerStep(std::uint8_t* list, const std::uint8_t* focus) {
    if (!focus || !*focus || InputBlocked()) return;
    const std::int64_t focused_at = NowMs();
    g_focus_ms.store(focused_at, std::memory_order_relaxed);
    if (Field<std::int32_t>(list, kItems) > 1 && OnScreen(list)) {
        g_choice_ms.store(focused_at, std::memory_order_relaxed);
        g_choice_list.store(list, std::memory_order_relaxed);
    }
    if (g_holding_row.load(std::memory_order_relaxed)) return;
    float x, y;
    if (!BbInput::MouseIsMenuPointer() || !BbInput::PointerOnStage(x, y)) return;
    if (g_scroll.load(std::memory_order_relaxed) && ItemUnder(list, x, y, false).index >= 0) {
        const int delta = g_scroll.exchange(0);
        const bool horizontal = Field<std::int32_t>(list, kLines) == 1;
        Call(Image(kScrollBy), list, horizontal ? delta : 0, horizontal ? 0 : delta);
    }
    bool fresh;
    Seen& seen = SeenBy(list, fresh);
    const bool moved = !fresh && (x != seen.x || y != seen.y);
    seen.x = x;
    seen.y = y;

    if (BbInput::TextEntryOpen()) return;
    if (g_log && !seen.described) {
        seen.described = true;
        host_log("pointer: list %p: %d items, %d cells x %d lines, cursor %d, scrolled %d/%d", static_cast<void*>(list),
                 Field<std::int32_t>(list, kItems), Field<std::int32_t>(list, kCells), Field<std::int32_t>(list, kLines),
                 CursorOf(list), Field<std::int32_t>(list, kScrollLines), Field<std::int32_t>(list, kAlong));
        ItemUnder(list, x, y, true);
    }
    const bool click = ClickWaiting();
    if (Field<std::int32_t>(list, kLines) == 1 && Field<std::int32_t>(list, kCells) == 1 &&
        Field<std::int32_t>(list, kItems) > 1) {
        if (click) SpinnerClick(list, x, y);
        return;
    }
    if (!moved && !click) return;
    if (DirectionHeld() && !(click && BbEngine::ChangeAppearance::PreviewShown())) {
        static int told = 0;
        if (g_log && told++ < 60) host_log("pointer: list %p: not hovered, a direction is held", static_cast<void*>(list));
        return;
    }
    const Pointed p = ItemUnder(list, x, y, false);
    if (p.index < 0) return;
    if (click) g_click.store(false, std::memory_order_relaxed);
    const int was = CursorOf(list);
    int now = was;
    if (p.index != was) {

        const int along = Field<std::int32_t>(list, kAlong), lines = Field<std::int32_t>(list, kScrollLines);
        const auto answer = std::int32_t(Call(Image(kSetCursor), list, p.index));
        const int back_along = along - Field<std::int32_t>(list, kAlong);
        const int back_lines = lines - Field<std::int32_t>(list, kScrollLines);
        if (answer == 1 && (back_along || back_lines)) {
            Call(Image(kScrollBy), list, back_along, back_lines);

            Call(Image(kHideCursors), list);
            ShowCursorOf(list, p.cell);
        }
        now = CursorOf(list);
        if (g_log) host_log("pointer: list %p hover %d -> %d (SetCursor %d)", static_cast<void*>(list), was, now, answer);
    }

    if (click && now == p.index) {
        const std::uint32_t press = ClickMeaning(list, p, x, y);
        g_press.store(press, std::memory_order_relaxed);
        if (g_log) host_log("pointer: list %p click on item %d -> pad %04x", static_cast<void*>(list), p.index, press);
    }
    if (now != was) CursorMoved(list, now);
}

// ChrMake_MultiSlider owns a vector of Slider dialogs, not a list component.
// Its update snapshots the selected index before reading navigation. Handle
// pointer selection at that navigation call so the original update still sees
// the index change and updates its child stack, highlight and selection sound.
constexpr std::size_t kMultiUpdate = 0x1b7a750, kMultiReadDirection = 0x1b7a7b5;
constexpr std::size_t kReadDirection = 0x1adfdd0, kMultiThunk = 0x1ac1e02;
constexpr std::size_t kSliderUpdate = 0x1adc970, kSliderSet = 0x1adcaa0;
constexpr std::size_t kMultiCursor = 0xa74, kMultiBegin = 0xa80, kMultiEnd = 0xa88;
constexpr std::size_t kDialogRoot = 0x100;
constexpr std::size_t kSliderBar = 0xa70;
constexpr std::size_t kSliderLeft = 0xb30, kSliderRight = 0xb90;
constexpr std::size_t kAppearanceRowVtable = 0x539b6e0, kEmbeddedSlider = 0xa70;
constexpr std::size_t kSliderMin = 0xbf0, kSliderMax = 0xbf4, kSliderValue = 0xbf8;
struct MultiContext { std::uint64_t dialog = 0, focus = 0; };
thread_local MultiContext g_multi;
std::atomic<bool> g_native_sliders{false};
std::atomic<std::uint64_t> g_slider_epoch{0};
struct SliderCapture {
    std::uint64_t owner = 0, row = 0, epoch = 0;
    int index = -1;
    Rect track;
};
// Accessed only by native menu callbacks; input cancellation uses an epoch.
thread_local SliderCapture g_slider_capture;


std::uint64_t WrappedObjectAt(std::uint64_t wrapper) {
    std::uint32_t type = 0; std::uint64_t data = 0, object = 0;
    return Read(wrapper + kValueType, type) && (type & 0x8f) == 10 &&
        Read(wrapper + kValueData, data) && Read(data + kDisplayObject, object) ? object : 0;
}

std::uint64_t SliderFromRow(std::uint64_t row) {
    if (VirtualFunction(row, 0x18) == Image(kSliderUpdate)) return row;
    // ChrMake_MultiSlider stores appearance row dialogs. Their update is the
    // base child-stack update; the actual Slider dialog is embedded at +0xa70.
    std::uint64_t vtable = 0;
    if (Read(row, vtable) && vtable == Image(kAppearanceRowVtable) &&
        VirtualFunction(row + kEmbeddedSlider, 0x18) == Image(kSliderUpdate)) {
        static bool reported = false;
        if (!reported) { reported = true; host_log("appearance pointer: embedded Slider resolved at row+0xa70"); }
        return row + kEmbeddedSlider;
    }
    return 0;
}

struct MultiHit { int index = -1, direction = 0; std::uint64_t row = 0; };
MultiHit MultiUnder(std::uint64_t dialog, float x, float y) {
    std::uint64_t begin = 0, end = 0;
    if (!Read(dialog + kMultiBegin, begin) || !Read(dialog + kMultiEnd, end) ||
        !begin || end < begin || (end - begin) % 8 || (end - begin) / 8 > 64) return {};
    for (std::uint64_t i = 0; i < (end - begin) / 8; ++i) {
        std::uint64_t row = 0;
        if (!Read(begin + 8 * i, row) || !row) continue;
        const auto slider = SliderFromRow(row);
        if (!slider) continue;
        const auto root = WrappedObjectAt(row + kDialogRoot);
        if (!root || !Shown(root)) continue;
        Rect rect;
        const auto left = WrappedObjectAt(slider + kSliderLeft);
        const auto right = WrappedObjectAt(slider + kSliderRight);
        // Inactive rows can hide their focus-only arrow drawings. Their
        // endpoint geometry still belongs to this visible appearance row.
        if (left && Contains(left, x, y, 2, &rect)) return {int(i), -1, slider};
        if (right && Contains(right, x, y, 2, &rect)) return {int(i), 1, slider};
        if (Contains(root, x, y, 0, &rect)) return {int(i), 0, slider};
    }
    return {};
}

bool SliderTrack(std::uint64_t row, Rect& track) {
    const auto bar = WrappedObjectAt(row + kSliderBar);
    const auto left = WrappedObjectAt(row + kSliderLeft);
    const auto right = WrappedObjectAt(row + kSliderRight);
    Rect bar_rect{}, l{}, r{};
    if (bar) Contains(bar, -100000, -100000, 0, &bar_rect);
    if (left) Contains(left, -100000, -100000, 0, &l);
    if (right) Contains(right, -100000, -100000, 0, &r);
    // Arrow geometry is stable even when the Bar's timeline animates its fill.
    // The native Bar supplies vertical bounds without making the whole label
    // clickable. Hidden focus-only arrows still retain their layout geometry.
    const bool bar_valid = bar_rect.right > bar_rect.left && bar_rect.bottom > bar_rect.top;
    if (l.right > l.left && r.right > r.left && r.left > l.right + 4) {
        track = {l.right, bar_valid ? bar_rect.top : std::min(l.top,r.top),
                 r.left, bar_valid ? bar_rect.bottom : std::max(l.bottom,r.bottom)};
    } else if (bar_valid) track = bar_rect;
    else return false;
    return std::isfinite(track.left) && std::isfinite(track.right) &&
        std::isfinite(track.top) && std::isfinite(track.bottom) &&
        track.right > track.left && track.bottom > track.top;
}

bool SetSliderAt(std::uint64_t row, const Rect& track, float x) {
    std::int32_t low = 0, high = 0, current = 0;
    if (!std::isfinite(x) || track.right <= track.left ||
        !Read(row + kSliderMin,low) || !Read(row + kSliderMax,high) ||
        !Read(row + kSliderValue,current) || high <= low) return false;
    const double ratio = std::clamp(double(x-track.left)/double(track.right-track.left),0.0,1.0);
    const auto next = std::int32_t(std::int64_t(low) + std::llround(ratio * (std::int64_t(high)-low)));
    if (next != current) Call(Image(kSliderSet),row,next,1,0x190,Image(0x493387b));
    return true;
}

bool ContinueSliderDrag(std::uint64_t owner, float x) {
    if (!g_slider_capture.row || g_slider_capture.owner != owner) return false;
    if (g_slider_capture.epoch != g_slider_epoch.load(std::memory_order_relaxed) ||
        !BbInput::PointerLeftHeld() || VirtualFunction(g_slider_capture.row,0x18) != Image(kSliderUpdate)) {
        g_slider_capture = {};return false;
    }
    return SetSliderAt(g_slider_capture.row,g_slider_capture.track,x);
}

bool BeginSliderDrag(std::uint64_t owner, std::uint64_t row, int index, float x, float y) {
    Rect track;
    if (!SliderTrack(row,track) || x < track.left || x > track.right ||
        y < track.top-4 || y > track.bottom+4) return false;
    if (!SetSliderAt(row,track,x)) return false;
    if (BbInput::PointerLeftHeld())
        g_slider_capture = {owner,row,g_slider_epoch.load(std::memory_order_relaxed),index,track};
    return true;
}

bool ApplySliderArrow(std::uint64_t row, int direction) {
    std::int32_t value = 0, low = 0, high = 0;
    if (!Read(row + kSliderValue, value) || !Read(row + kSliderMin, low) ||
        !Read(row + kSliderMax, high) || low > high) return false;
    const auto next = std::int32_t(std::clamp<std::int64_t>(std::int64_t(value) + direction, low, high));
    // Native SetValue updates text/bar and notifies the appearance callback.
    Call(Image(kSliderSet), row, next, 1, 0x190, Image(0x493387b));
    return true;
}

std::int64_t __attribute__((sysv_abi)) OnSliderUpdate(std::uint64_t, const std::uint64_t* saved) {
    const auto row = saved[5];
    const auto* focus = reinterpret_cast<const std::uint8_t*>(saved[4]);
    float x = 0, y = 0;
    if (!focus || !*focus || !BbEngine::ChangeAppearance::PreviewShown() || InputBlocked() ||
        BbInput::TextEntryOpen() || !BbInput::MouseIsMenuPointer() || !BbInput::PointerOnStage(x,y)) {
        if (g_slider_capture.row == row) g_slider_capture = {};
        return 0;
    }
    if (ClickWaiting()) g_slider_capture = {};
    if (g_slider_capture.row == row && g_slider_capture.owner == 0 && ContinueSliderDrag(0,x)) return 0;
    if (!ClickWaiting()) return 0;
    const auto root = WrappedObjectAt(row + kDialogRoot);
    if (!root || !Shown(root)) return 0;
    Rect rect;
    for (const auto direction : {-1,1}) {
        const auto arrow = WrappedObjectAt(row + (direction < 0 ? kSliderLeft : kSliderRight));
        if (arrow && Contains(arrow,x,y,2,&rect) && g_click.exchange(false,std::memory_order_relaxed)) {
            ApplySliderArrow(row,direction);return 0;
        }
    }
    if (BeginSliderDrag(0,row,-1,x,y)) g_click.store(false,std::memory_order_relaxed);
    return 0;
}

bool MultiPointerStep(std::uint64_t dialog, const std::uint8_t* focus) {
    if (!focus || !*focus || !BbEngine::ChangeAppearance::PreviewShown() ||
        InputBlocked() || BbInput::TextEntryOpen() || !BbInput::MouseIsMenuPointer()) {
        if (g_slider_capture.owner == dialog) g_slider_capture = {};
        return false;
    }
    float x = 0, y = 0;
    if (!BbInput::PointerOnStage(x, y)) {
        if (g_slider_capture.owner == dialog) g_slider_capture = {};
        return false;
    }
    if (ClickWaiting()) g_slider_capture = {};
    if (g_slider_capture.row && g_slider_capture.owner == dialog) {
        // Check membership before touching a retained row: a submenu can close
        // or rebuild its vector while the mouse is held.
        std::uint64_t begin=0,end=0,row=0;
        const int index=g_slider_capture.index;
        const bool member=index>=0 && Read(dialog+kMultiBegin,begin) && Read(dialog+kMultiEnd,end) &&
            begin && end>=begin && (end-begin)/8<=64 && std::uint64_t(index)<(end-begin)/8 &&
            Read(begin+8*index,row) && SliderFromRow(row)==g_slider_capture.row;
        if (!member) g_slider_capture={};
        else if (ContinueSliderDrag(dialog,x)) {
            std::memcpy(reinterpret_cast<void*>(dialog+kMultiCursor),&index,sizeof index);
            return true;
        }
    }
    const bool click = ClickWaiting();
    if (g_holding_row.load(std::memory_order_relaxed) && !click) return false;
    bool fresh;
    auto& seen = SeenBy(reinterpret_cast<const std::uint8_t*>(dialog), fresh);
    const bool moved = !fresh && (seen.x != x || seen.y != y);
    seen.x = x; seen.y = y;
    if ((!moved && !click) || (!click && DirectionHeld())) return false;
    const auto hit = MultiUnder(dialog, x, y);
    if (hit.index < 0) {
        static unsigned missed = 0;
        if (click && missed++ < 4) {
            std::uint64_t begin = 0, end = 0;
            Read(dialog + kMultiBegin, begin); Read(dialog + kMultiEnd, end);
            host_log("appearance pointer: no slider row hit at %.0f,%.0f; dialog=%llx rows=%llu", x, y,
                static_cast<unsigned long long>(dialog), static_cast<unsigned long long>(end >= begin ? (end-begin)/8 : 0));
        }
        return false;
    }
    // This executes after the native update saved its previous cursor. The
    // original code performs child activation and cursor visibility afterwards.
    std::memcpy(reinterpret_cast<void*>(dialog + kMultiCursor), &hit.index, sizeof hit.index);
    if (click && g_click.exchange(false, std::memory_order_relaxed)) {
        if (hit.direction) ApplySliderArrow(hit.row, hit.direction);
        else BeginSliderDrag(dialog,hit.row,hit.index,x,y);
        static unsigned reports = 0;
        if (reports++ < 8) host_log("appearance pointer: multi-slider row %d, %s", hit.index,
                                  hit.direction < 0 ? "decrement 1" : hit.direction > 0 ? "increment 1" : "selected");
    }
    return true;
}

std::int64_t __attribute__((sysv_abi)) OnMultiUpdate(std::uint64_t, const std::uint64_t* saved) {
    g_multi = {saved[5], saved[4]};
    return 0;
}
std::int64_t __attribute__((sysv_abi)) OnMultiDirection(std::uint64_t, const std::uint64_t* saved) {
    if (g_multi.dialog && g_multi.focus == saved[5] &&
        MultiPointerStep(g_multi.dialog, reinterpret_cast<const std::uint8_t*>(saved[5]))) {
        const_cast<std::uint64_t*>(saved)[-1] = 0;
        return 1;
    }
    return 0;
}

constexpr std::size_t kPreviewUpdate = 0x1b07870, kPreviewZoom = 0x1db2990;
constexpr std::size_t kPreviewRates = 0x47349e0, kPreviewRotate = 0x1db2780;
using PreviewZoomFn = void (__attribute__((sysv_abi)) *)(std::uint64_t, float);

bool ApplyPreviewZoom(std::uint64_t dialog, float detents, PreviewZoomFn zoom) {
    if (!std::isfinite(detents) || detents == 0) return false;
    std::int32_t mode = -1;
    float rate = 0;
    if (!Read(dialog + 0xae8, mode) || mode < 0 || mode > 2 ||
        !Read(Image(kPreviewRates) + mode * 12, rate) || !std::isfinite(rate) || rate <= 0) return false;
    std::uint64_t models[4]{};
    // Validate the complete model set before updating any of the four views.
    for (unsigned i = 0; i < 4; ++i)
        if (!Read(dialog + 0xac8 + i * 8, models[i]) || !models[i]) return false;
    // One detent keeps the previous 80 ms sensitivity (2.4 native 30 Hz
    // increments). The game's own setter clamps the resulting camera distance.
    const float amount = detents * rate * 2.4f;
    if (!std::isfinite(amount)) return false;
    for (auto model : models) zoom(model, amount);
    return true;
}

using PreviewRotateFn = void (__attribute__((sysv_abi)) *)(std::uint64_t, float, float);

bool ApplyPreviewRotation(std::uint64_t dialog, EditorDrag::Motion motion, PreviewRotateFn rotate) {
    if (!std::isfinite(motion.x) || !std::isfinite(motion.y) ||
        (motion.x == 0 && motion.y == 0)) return false;
    std::int32_t mode = -1;
    float rates[3]{};
    if (!Read(dialog + 0xae8, mode) || mode < 0 || mode > 2 ||
        !Read(Image(kPreviewRates) + mode * 12, rates) ||
        !std::isfinite(rates[1]) || !std::isfinite(rates[2]) || rates[1] <= 0 || rates[2] <= 0) return false;
    std::uint64_t models[4]{};
    for (unsigned i = 0; i < 4; ++i)
        if (!Read(dialog + 0xac8 + i * 8, models[i]) || !models[i]) return false;
    // Mouse drag: right adds yaw; positive Y follows the requested preview direction.
    // Native rotation wraps yaw and clamps pitch to the selected preview mode.
    const float yaw = motion.x * rates[2], pitch = motion.y * rates[1];
    if (!std::isfinite(yaw) || !std::isfinite(pitch)) return false;
    for (auto model : models) rotate(model, yaw, pitch);
    return true;
}

std::int64_t __attribute__((sysv_abi)) OnPreviewUpdate(std::uint64_t, const std::uint64_t* saved) {
    const float wheel = g_preview_wheel.Take();
    EditorDrag::Motion drag;
    { std::scoped_lock lock(g_preview_drag_lock); drag = g_preview_drag.Take(); }
    if (BbEngine::ChangeAppearance::PreviewShown() && !InputBlocked() &&
        !BbInput::TextEntryOpen() && BbInput::MouseIsMenuPointer()) {
        if (wheel) ApplyPreviewZoom(saved[5], wheel, reinterpret_cast<PreviewZoomFn>(Image(kPreviewZoom)));
        if (g_preview_rotation_hook && (drag.x != 0 || drag.y != 0))
            ApplyPreviewRotation(saved[5], drag, reinterpret_cast<PreviewRotateFn>(Image(kPreviewRotate)));
    }
    return 0;
}

bool InstallPreviewZoom(std::uint8_t* image, std::size_t size) {
    constexpr std::uint8_t entry[] = {0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,
        0x41,0x55,0x41,0x54,0x53,0x48,0x81,0xec,0x88,0,0,0};
    constexpr std::uint8_t setter[] = {0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,
        0x41,0x54,0x53,0x48,0x83,0xec,0x50};
    if (size < kPreviewZoom + sizeof setter ||
        std::memcmp(image + kPreviewZoom, setter, sizeof setter)) return false;
    BbEngine::Hook hook;
    if (!BbEngine::Prepare(hook, image, size, kPreviewUpdate, entry, OnPreviewUpdate)) return false;
    BbEngine::Commit(std::span(&hook, 1));
    g_preview_zoom_hook = true;
    // Rotation uses the same native preview update; do not install a second
    // hook over its prologue. Keep zoom available if only this signature differs.
    g_preview_rotation_hook = size >= kPreviewRotate + sizeof setter &&
        !std::memcmp(image + kPreviewRotate, setter, sizeof setter);
    host_log("appearance preview: proportional wheel zoom; all detents consumed at native update, no timed queue");
    host_log("appearance preview: %s", g_preview_rotation_hook ?
        "proportional two-axis drag; no stick dead zone or held input" :
        "native rotation signature differs; using stick fallback");
    return true;
}

bool InstallMulti(std::uint8_t* image, std::size_t size) {
    constexpr std::uint8_t entry[] = {0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,
        0x41,0x54,0x53,0x48,0x81,0xec,0x88,0,0,0};
    constexpr std::uint8_t padding[] = {0x66,0x66,0x66,0x66,0x66,0x2e,0x0f,0x1f,0x84,0,0,0,0,0};
    constexpr std::uint8_t call[] = {0xe8,0x16,0x56,0xf6,0xff};
    constexpr std::uint8_t setter[] = {0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,
        0x41,0x54,0x53,0x48,0x83,0xec,0x58};
    if (size < kMultiReadDirection + sizeof call ||
        std::memcmp(image + kMultiThunk, padding, sizeof padding) ||
        std::memcmp(image + kMultiReadDirection, call, sizeof call) ||
        std::memcmp(image + kSliderSet, setter, sizeof setter)) return false;
    constexpr std::uint8_t slider_entry[] = {0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,
        0x53,0x50,0x49,0x89,0xf6,0x48,0x89,0xfb};
    std::array<BbEngine::Hook, 2> hooks;
    if (!BbEngine::Prepare(hooks[0], image, size, kMultiUpdate, entry, OnMultiUpdate) ||
        !BbEngine::Prepare(hooks[1], image, size, kSliderUpdate, slider_entry, OnSliderUpdate)) {
        for (auto& h : hooks) BbEngine::Discard(h);
        return false;
    }
    auto* stub = BbEngine::TailStub(OnMultiDirection, Image(kReadDirection));
    if (!stub) { for (auto& h : hooks) BbEngine::Discard(h); return false; }
    std::uint8_t jump[14] = {0xff,0x25};
    const auto target = reinterpret_cast<std::uint64_t>(stub);
    std::memcpy(jump + 6, &target, 8);
    std::uint8_t redirect[5] = {0xe8};
    const auto delta = std::int32_t(std::int64_t(kMultiThunk) - std::int64_t(kMultiReadDirection + 5));
    std::memcpy(redirect + 1, &delta, 4);
    // Redirect only this call. The game's shared navigation reader is untouched.
    if (!BbEngine::WriteCode(image + kMultiThunk, jump, sizeof jump) ||
        !BbEngine::WriteCode(image + kMultiReadDirection, redirect, sizeof redirect)) {
        BbEngine::WriteCode(image + kMultiThunk, padding, sizeof padding);
        BbEngine::FreeStub(stub); for (auto& h : hooks) BbEngine::Discard(h); return false;
    }
    BbEngine::Commit(hooks);
    g_native_sliders.store(true,std::memory_order_relaxed);
    host_log("appearance pointer: ChrMake_MultiSlider hooked; native row selection, direct slider dragging and single-unit arrow clicks");
    return true;
}

std::int64_t __attribute__((sysv_abi)) OnListUpdate(std::uint64_t, const std::uint64_t* saved) {
    auto* list = reinterpret_cast<std::uint8_t*>(saved[5]);
    const auto* focus = reinterpret_cast<const std::uint8_t*>(saved[4]);
    PointerStep(list, focus);
    BbEngine::Options::ListUpdate(list, focus && *focus);
    // Suppress UI direction handling only; the gameplay pad keeps its world movement.
    if (!BbEngine::ChangeAppearance::PreviewShown() &&
        !BbOverlay::MenuOpen() && !BbInput::TextEntryOpen() &&
        BbInputPolicy::FilterList(BbSettings::Get().mouse_menu, FrontEnd(),
            BbInput::KeyboardMoving(), host_input_device() == InputDevice::Pad)) {
        static const std::uint8_t no_directions = 0;
        const_cast<std::uint64_t*>(saved)[4] = reinterpret_cast<std::uint64_t>(&no_directions);
    }
    return 0;
}
}

bool Install(std::uint8_t* image, std::size_t size) {
    g_image = image;
    const char* log = std::getenv("BB_POINTER_LOG");
    g_log = log && log[0] == '1';
    BbEngine::Hook hook;
    if (!BbEngine::Prepare(hook, image, size, kListUpdate, kListUpdateEntry, OnListUpdate)) {
        host_log("pointer: off, the menus' list update is not 1.09's");
        return false;
    }
    BbEngine::Commit(std::span(&hook, 1));
    host_log("pointer: the menus' lists follow the mouse");
    if (!InstallPreviewZoom(image, size)) host_log("appearance preview: proportional zoom unavailable; using trigger fallback");
    if (!InstallMulti(image, size)) host_log("appearance pointer: multi-slider hook unavailable (signature or allocation)");
    return true;
}

bool FrontEnd() {
    std::uint8_t started = 1;
    const std::uint64_t flow = Flow();
    return flow && Read(flow + kFlowStarted, started) && !started;
}

bool Navigating() {
    if (BbEngine::ChangeAppearance::PreviewShown()) return true;
    const bool front = FrontEnd(), open = !front && MenuOpen();
    const bool choice = !front && !open && NowMs() - g_choice_ms.load(std::memory_order_relaxed) < kNavigatingMs;
    const int why = front ? 1 : open ? 2 : choice ? 3 : 0;
    static int last = -1, told = 0;
    if (why != last && told < 40) {
        ++told;
        static const char* const kWhy[] = {"off", "the front end", "a menu the game opened", "a list with a choice"};
        if (why == 3) host_log("menu keys: %s (%p)", kWhy[why], static_cast<const void*>(g_choice_list.load(std::memory_order_relaxed)));
        else host_log("menu keys: %s", kWhy[why]);
    }
    last = why;
    return why != 0;
}

bool NativeSliders() { return g_native_sliders.load(std::memory_order_relaxed); }
void CancelSliderDrag() { g_slider_epoch.fetch_add(1,std::memory_order_relaxed); }

void HoldRow(bool hold) { g_holding_row.store(hold, std::memory_order_relaxed); }

void Click() {
    g_click_ms.store(NowMs(), std::memory_order_relaxed);
    g_click.store(true, std::memory_order_relaxed);
}

void ResetPreview() { ClearPreviewRotation(); ClearPreviewZoom(); g_press.fetch_or(PadBits::kR3, std::memory_order_relaxed); }
bool ZoomPreview(float wheel) {
    if (!g_preview_zoom_hook) return false;
    g_preview_wheel.Push(wheel);
    return true;
}
void ClearPreviewZoom() { g_preview_wheel.Reset(); }
bool RotatePreview(float dx, float dy, float gain) {
    if (!g_preview_rotation_hook) return false;
    std::scoped_lock lock(g_preview_drag_lock);
    g_preview_drag.Push(dx, dy, gain);
    return true;
}
void ClearPreviewRotation() {
    std::scoped_lock lock(g_preview_drag_lock);
    g_preview_drag.Reset();
}


void CancelClicks() {
    g_slider_epoch.fetch_add(1,std::memory_order_relaxed);
    g_click.store(false, std::memory_order_relaxed);
    g_press.store(0, std::memory_order_relaxed);
    std::scoped_lock lock(g_editor_click_lock);
    g_editor_clicks.Reset();
}

std::uint32_t TakePress() {
    const std::uint32_t press = g_press.exchange(0);
    std::scoped_lock lock(g_editor_click_lock);
    if (!BbEngine::ChangeAppearance::PreviewShown() || !BbSettings::Get().mouse_menu ||
        BbInput::TextEntryOpen() || BbOverlay::CapturesInput()) {
        g_editor_clicks.Reset();
        return press;
    }
    // A one-read click is easily lost between native menu frames. Emit a
    // short press/release per click, below the native held-key repeat delay.
    g_editor_clicks.Push(press);
    return g_editor_clicks.Poll(std::uint64_t(NowMs()));
}
}

std::uint32_t menu_confirm_button() {
    return MenuPointer::RegionDecidesWithCircle() ? PadBits::kCircle : PadBits::kCross;
}

std::uint32_t menu_back_button() { return menu_confirm_button() == PadBits::kCircle ? PadBits::kCross : PadBits::kCircle; }

bool menu_pointer_in_menu() { return MenuPointer::InMenu(); }

void menu_pointer_scroll(int direction) { MenuPointer::g_scroll.store(direction); }
void menu_pointer_selection_sound() {
    if (!MenuPointer::g_image) return;
    auto* sound = reinterpret_cast<std::int32_t*>(MenuPointer::Image(MenuPointer::kMenuSound));
    if (sound[1] <= 0) {
        sound[0] = sound[1] = 0;
        *reinterpret_cast<std::uint64_t*>(MenuPointer::Image(MenuPointer::kMenuSoundLabel)) = MenuPointer::Image(MenuPointer::kCursorMoveLabel);
    }
}
