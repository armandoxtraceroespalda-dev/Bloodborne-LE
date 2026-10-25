// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace SprintTurn {
struct State {
    double angle = 0;
    std::int64_t at = 0;
    int target_x = 0, target_y = 0;
    bool active = false;
};
struct Axes { int x, y; };
struct Config { int degrees_per_second=800, release_delay_ms=0; bool circular=false; };

// Round compatible keyboard turns in every movement mode. Retain
// the square stick boundary (one axis fully pushed) throughout the turn.
inline Axes Apply(State& state, int x, int y, bool /*sprint*/, std::int64_t now_us, Config config={}) {
    constexpr double pi = 3.14159265358979323846;
    const double rate = std::clamp(config.degrees_per_second,0,5000)*pi/180;
    constexpr int reach = 127;
    // No key-release buffering. Releasing movement stops immediately.
    const double target = std::atan2(double(y), double(x));
    const auto elapsed = now_us-state.at;
    const bool reversal = state.target_x*x < 0 || state.target_y*y < 0;
    if (!x && !y) {
        state = {};
        return {x*reach, y*reach};
    }
    if (!state.active || reversal || rate==0 || elapsed < 0 || elapsed > 250000) {
        state.angle = target;
    } else {
        const double difference = std::remainder(target-state.angle, 2*pi);
        const double step = std::min(rate*double(elapsed)/1e6, pi/6);
        state.angle += std::clamp(difference, -step, step);
        state.angle = std::remainder(state.angle, 2*pi);
    }
    state.active = true;
    state.at = now_us;
    state.target_x = x;
    state.target_y = y;
    const double cx = std::cos(state.angle), cy = std::sin(state.angle);
    const double scale = config.circular ? reach : reach/std::max(std::abs(cx), std::abs(cy));
    return {int(std::lround(cx*scale)), int(std::lround(cy*scale))};
}
}
