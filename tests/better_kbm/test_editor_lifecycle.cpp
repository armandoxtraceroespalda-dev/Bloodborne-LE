// SPDX-License-Identifier: GPL-3.0-or-later
// Real menu-step observation and editor lifetime; only guest memory and calls are synthetic.
#define TEST_POINTER
#include "engine_state.cpp"
#include "change_appearance.cpp"
#include <cassert>
#include <vector>
static std::vector<std::uint8_t> memory(0x5900000);
static int destroyed = 0;
static auto base(){return reinterpret_cast<std::uint64_t>(memory.data());}
bool test_safe_read(const void* p, void* out, std::size_t n) {
    const auto at = reinterpret_cast<std::uint64_t>(p);
    if (at < base() || at - base() > memory.size() || n > memory.size() - (at - base())) return false;
    std::memcpy(out, p, n);return true;
}
std::int64_t test_guest_call(void* fn, const std::int64_t*, std::size_t) {
    assert(reinterpret_cast<std::uint64_t>(fn) == base() + 0x410000);
    ++destroyed;return 0;
}
int main() {
    using namespace BbEngine;
    StateInitialize(base());
    assert(ChangeAppearance::Watch(memory.data()));
    const auto step = base() + 0x5800000, name = base() + 0x4900000, table = step + 0x100;
    const char16_t caption[] = u"ChrMakeCommandList";
    std::memcpy(reinterpret_cast<void*>(name), caption, sizeof caption);
    std::memcpy(reinterpret_cast<void*>(step), &table, 8);
    const auto destructor = base() + 0x410000;
    std::memcpy(reinterpret_cast<void*>(table), &destructor, 8);
    *menu_step_count(step) = 2;
    const std::uint64_t kind = 7;
    std::memcpy(reinterpret_cast<void*>(step + 0x28), &kind, 8);
    std::memcpy(reinterpret_cast<void*>(step + 0x30), &name, 8);
    std::uint64_t saved[6]{};saved[5] = step;saved[3] = kind;saved[2] = name;
    menu_steps_notify(saved);ChangeAppearance::Tick();
    assert(ChangeAppearance::PreviewShown() && *menu_step_count(step) == 3);
    for (int frame = 0; frame < 300; ++frame) ChangeAppearance::Tick();
    assert(ChangeAppearance::PreviewShown());
    // When the game's owners close the screen, release exactly the observer's remaining reference.
    *menu_step_count(step) = 1;ChangeAppearance::Tick();
    assert(!ChangeAppearance::PreviewShown() && destroyed == 1);
    ChangeAppearance::Tick();assert(destroyed == 1);
    std::puts("PASS: production observer recognizes ChrMakeCommandList, holds the editor across frames and releases it once on close.");
}
