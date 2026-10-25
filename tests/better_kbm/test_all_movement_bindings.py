#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Compile the actual keyboard movement block with deterministic clock/settings."""
from pathlib import Path
import argparse
import os
import subprocess
import tempfile


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[2])
    args=parser.parse_args(); engine=args.root/'engine'
    text=(engine/'gpu/shim/input/bindings.cpp').read_text()
    start=text.index('    const int x =',text.index('void host_bindings_apply('))
    end=text.index('    if (any) p.connected',start)
    movement=text[start:end]
    clock_start=movement.index('    const auto now =')
    clock_end=movement.index('    const auto& settings=',clock_start)
    movement=movement[:clock_start]+movement[clock_end:]
    harness=r'''
#include "sprint_turn.h"
#include <cassert>
#include <cmath>
#include <cstdint>
#include <cstdio>
enum {kBindMoveR,kBindMoveL,kBindMoveB,kBindMoveF,kBindRoll,kBindWalk,kBindCount};
constexpr int kStickRest=127;
constexpr float kWalkPush=.6f;
struct Settings {int stick_turn_rate=900,stick_release_delay_ms=0;bool stick_circular=true;};
struct BbSettings {static Settings& Get(){static Settings value;return value;}};
struct PadState {std::uint8_t lx=127,ly=127,rx=91,ry=145;};
SprintTurn::State sprint_turn;
void apply(const bool held[kBindCount],PadState& p,std::int64_t now) {
''' + movement + r'''
}
int main(){
    bool held[kBindCount]={}; held[kBindMoveF]=true;
    PadState p;apply(held,p,1000000); assert(p.lx==127&&p.ly==0);
    held[kBindMoveL]=true;p={};apply(held,p,1010000);
    assert(p.lx<127&&p.lx>100&&p.ly<5); // Normal movement is interpolated.
    held[kBindWalk]=true;p={};apply(held,p,1020000);
    assert(p.lx>90&&p.ly<60); // Walking retains the filtered angle, not raw A+W.
    assert(std::abs(std::hypot(double(p.lx)-127,double(p.ly)-127)-76.2)<1);
    assert(p.rx==91&&p.ry==145); // This block does not alter camera axes.
    held[kBindMoveL]=false;held[kBindMoveR]=true;p={};apply(held,p,1021000);
    assert(p.lx>127&&p.ly<127&&std::abs(sprint_turn.angle+3.141592653589793/4)<1e-9);
    held[kBindWalk]=false;held[kBindRoll]=true;p={};apply(held,p,1030000);
    assert(p.lx>127&&p.ly<127); // Sprint transition preserves the chosen direction.
    held[kBindMoveR]=held[kBindMoveF]=held[kBindRoll]=false;p={};apply(held,p,1031000);
    assert(p.lx==127&&p.ly==127&&!sprint_turn.active);
    std::puts("PASS: actual keyboard movement block: normal/slow walk, sprint transition, opposing reversal, stop and unchanged camera axes.");
}
'''
    with tempfile.TemporaryDirectory() as temp:
        source=Path(temp)/'movement.cpp'; output=Path(temp)/('movement.exe' if os.name=='nt' else 'movement')
        source.write_text(harness)
        subprocess.run([os.environ.get('CXX','c++'),'-std=c++20','-Wall','-Wextra','-Werror','-I',str(engine/'gpu/shim/input'),str(source),'-o',str(output)],check=True)
        subprocess.run([str(output)],check=True)


if __name__=='__main__': main()
