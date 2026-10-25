// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace EditorDrag {
// Pad reads can outnumber menu updates. Keep each press and its release long
// enough for the game's 30 Hz UI, rather than consuming it at the next read.
struct Steps {
    int pending = 0, held = 0;
    std::uint64_t until = 0, ready = 0;
    void Reset() { pending = held = 0; until = ready = 0; }
    void Push(int steps) { pending = std::clamp(pending + steps, -32, 32); }
    int Poll(std::uint64_t now) {
        if (held) {
            if (now < until) return held;
            held = 0;
            ready = now + 40;
            return 0;
        }
        if (!pending || now < ready) return 0;
        held = pending > 0 ? 1 : -1;
        pending -= held;
        until = now + 45;
        return held;
    }
};

// Accumulate wheel distance until the native preview update consumes it once.
// This stores displacement, not trigger duration: rapid detents are not merged
// into one press and cannot leave a timed zoom backlog after scrolling stops.
struct WheelZoom {
    std::atomic<float> pending{0};
    void Reset() { pending.store(0, std::memory_order_relaxed); }
    void Push(float wheel) {
        if (std::isfinite(wheel) && wheel != 0)
            pending.fetch_add(wheel, std::memory_order_relaxed);
    }
    float Take() { return pending.exchange(0, std::memory_order_relaxed); }
};

// Convert wheel detents to a short held trigger, surviving fast pad polling.
struct Zoom {
    float fraction = 0;
    int direction = 0;
    std::uint64_t until = 0;
    void Reset() { fraction = 0; direction = 0; until = 0; }
    int Poll(float wheel, std::uint64_t now) {
        if (std::isfinite(wheel) && wheel != 0) {
            if (fraction * wheel < 0) fraction = 0;
            fraction = std::clamp(fraction + wheel, -8.0f, 8.0f);
            const int steps = int(fraction);
            fraction -= float(steps);
            if (steps) {
                const int next = steps > 0 ? 1 : -1;
                // Refresh a single pulse instead of queuing wheel time. Eighty
                // milliseconds span two 30 Hz menu frames without a long tail.
                until = now + 80;
                direction = next;
            }
        }
        if (now >= until) direction = 0;
        return direction;
    }
};

// Editor clicks need both a visible press and a release between consecutive clicks.
struct Clicks {
    std::array<std::uint32_t, 8> queue{};
    unsigned head = 0, count = 0;
    std::uint32_t held = 0;
    std::uint64_t until = 0, ready = 0;
    void Reset() { head = count = 0; held = 0; until = ready = 0; }
    void Push(std::uint32_t button) {
        if (button && count < queue.size()) queue[(head + count++) % queue.size()] = button;
    }
    std::uint32_t Poll(std::uint64_t now) {
        if (held) {
            if (now < until) return held;
            held = 0; ready = now + 40; return 0;
        }
        if (!count || now < ready) return 0;
        held = queue[head]; head = (head + 1) % queue.size(); --count;
        until = now + 45;
        return held;
    }
};

// Keep both mouse axes as displacement until the preview frame consumes them.
// No dead zone, byte quantization, minimum stick deflection or held motion.
struct Motion { float x = 0, y = 0; };
struct DragRotation {
    Motion pending;
    void Reset() { pending = {}; }
    void Push(float dx, float dy, float gain) {
        if (!std::isfinite(gain) || gain <= 0) return;
        if (std::isfinite(dx)) pending.x += dx * gain / 127.f;
        if (std::isfinite(dy)) pending.y += dy * gain / 127.f;
    }
    Motion Take() { const auto value = pending; pending = {}; return value; }
};

struct Axes { std::uint8_t x = 127, y = 127; };
struct Rotation {
    float pending_x = 0, pending_y = 0;
    std::uint64_t next = 0, until = 0;
    Axes held;
    void Reset() { pending_x = pending_y = 0; next = until = 0; held = {}; }
    static std::uint8_t Axis(float counts, float gain) {
        if (counts == 0) return 127;
        // ChrMake_BG's native preview checks +/-0.2. Smaller right-stick
        // deflections are ignored even when they are valid pad input.
        const float amount = std::clamp(std::fabs(counts) * gain, 40.0f, 127.0f);
        return static_cast<std::uint8_t>(127 + (counts > 0 ? int(amount) : -int(amount)));
    }
    Axes Poll(float dx, float dy, std::uint64_t now, float gain) {
        if (std::isfinite(dx) && std::isfinite(dy)) {
            pending_x = std::clamp(pending_x + dx, -4096.0f, 4096.0f);
            pending_y = std::clamp(pending_y + dy, -4096.0f, 4096.0f);
        }
        if (now >= next && (pending_x != 0 || pending_y != 0)) {
            held = {Axis(pending_x, gain), Axis(pending_y, gain)};
            pending_x = pending_y = 0;
            next = now + 8;
            until = now + 50;
        }
        return now < until ? held : Axes{};
    }
};
}
