// SPDX-License-Identifier: GPL-3.0-or-later
#include "sprint_turn.h"
#include <array>
#include <cassert>
#include <cmath>
#include <cstdio>

int main() {
    assert(SprintTurn::Config{}.degrees_per_second==800);
    constexpr double pi = 3.14159265358979323846;
    constexpr std::array<SprintTurn::Axes,8> directions = {{{0,-1},{1,-1},{1,0},{1,1},
        {0,1},{-1,1},{-1,0},{-1,-1}}};
    int paths=0, reversals=0;
    for (bool sprint : {false, true}) for (bool circular : {false, true})
    for (int fps : {30,60,90,120,144,240})
    for (auto from : directions) for (auto to : directions) {
        SprintTurn::State state;
        SprintTurn::Config config{1200,0,circular};
        auto now = std::int64_t(1000000);
        SprintTurn::Apply(state,from.x,from.y,sprint,now,config);
        const bool opposite = from.x*to.x<0 || from.y*to.y<0;
        const double target = std::atan2(double(to.y),double(to.x));
        for (int tick=1; tick<=fps; ++tick) {
            double before=state.angle;
            now += 1000000/fps;
            auto axes=SprintTurn::Apply(state,to.x,to.y,sprint,now,config);
            assert(state.active && (axes.x || axes.y));
            assert(std::abs(axes.x)<=127 && std::abs(axes.y)<=127);
            if (circular) assert(std::abs(std::hypot(double(axes.x),double(axes.y))-127)<1);
            else assert(std::max(std::abs(axes.x),std::abs(axes.y))==127);
            if (opposite && tick==1) {
                assert(std::abs(std::remainder(state.angle-target,2*pi))<1e-9);
                ++reversals;
            } else if (!opposite) {
                double delta=std::abs(std::remainder(state.angle-before,2*pi));
                assert(delta<=std::min(1200*pi/180*(1000000/fps)/1e6,pi/6)+1e-9);
            }
        }
        assert(std::abs(std::remainder(state.angle-target,2*pi))<1e-9);
        auto stopped=SprintTurn::Apply(state,0,0,sprint,now+1000,config);
        assert(!state.active && stopped.x==0 && stopped.y==0);
        ++paths;
    }
    // Changing only the sprint button must preserve the same movement trajectory.
    SprintTurn::State mixed, normal;
    SprintTurn::Config config{900,0,true};
    for (int tick=0; tick<20; ++tick) {
        int x=tick<2?0:-1;
        auto time=std::int64_t(1000000+tick*10000);
        auto a=SprintTurn::Apply(mixed,x,-1,tick%2,time,config);
        auto b=SprintTurn::Apply(normal,x,-1,false,time,config);
        assert(a.x==b.x && a.y==b.y);
    }
    // A/D reversals in the middle of an unfinished turn remain immediate.
    auto reverse=SprintTurn::Apply(mixed,1,-1,false,1200000,config);
    assert(reverse.x>0 && reverse.y<0 && std::abs(mixed.angle+pi/4)<1e-9);
    // A configured sprint release grace must never create ordinary walking drift.
    SprintTurn::State grace;
    SprintTurn::Apply(grace,0,-1,false,2000000,{900,60,true});
    auto stop=SprintTurn::Apply(grace,0,0,false,2001000,{900,60,true});
    assert(stop.x==0 && stop.y==0 && !grace.active);
    SprintTurn::Apply(grace,0,-1,true,2100000,{900,60,true});
    auto held=SprintTurn::Apply(grace,0,0,true,2101000,{900,60,true});
    assert(held.x==0 && held.y==0 && !grace.active);
    stop=SprintTurn::Apply(grace,0,0,true,2160001,{900,60,true});
    assert(stop.x==0 && stop.y==0 && !grace.active);
    // Zero turning speed still disables interpolation in both movement modes.
    for (bool sprint : {false,true}) {
        SprintTurn::State state;
        SprintTurn::Apply(state,0,-1,sprint,3000000,{0,0,false});
        auto instant=SprintTurn::Apply(state,-1,0,sprint,3000001,{0,0,false});
        assert(instant.x==-127 && instant.y==0);
    }
    std::printf("PASS: %d movement paths, %d immediate reversals; six frame rates, circular/square range, sprint transitions and stop/release behavior.\n",paths,reversals);
}
