#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Compile the production IconSpec resolver with mocked binding services."""
from pathlib import Path
import argparse, subprocess, tempfile
p=argparse.ArgumentParser()
p.add_argument('--engine',type=Path,required=True)
p.add_argument('--cxx',default='g++')
a=p.parse_args()
engine=a.engine.resolve()
source=(engine/'gpu/shim/engine/key_prompts.cpp').read_text()
def block(start):
    at=source.index(start); opening=source.index('{',at); depth=1; end=opening+1
    while depth:
        if source[end]=='{': depth+=1
        elif source[end]=='}': depth-=1
        end+=1
    return source[at:end]
enum=block('enum Icon : int')+';'
state=block('struct State')+';'
resolver=block('std::optional<Keycap::Spec> IconSpec(')
program='''#include <optional>
#include <initializer_list>
#include <cassert>
#include <cstdio>
#include "engine/keycap_art.h"
#include "input/bindings.h"
'''+enum+'\n'+state+'''
constexpr unsigned kPadCross=0x4000,kPadCircle=0x2000;
std::optional<Keycap::Spec> PadSpec(int icon){return Keycap::Spec{Keycap::Spec::Pad,{},icon};}
std::optional<Keycap::Spec> ActionSpec(int action){return Keycap::Spec{Keycap::Spec::Key,{std::to_string(action)}};}
std::optional<Keycap::Spec> Caps(std::initializer_list<int>){return Keycap::Spec{Keycap::Spec::Keys,{}};}
'''+resolver+'''
int main(){
 State state;state.keys=true;state.menu=true;state.preview=true;
 auto in=IconSpec(kL2,state),out=IconSpec(kR2,state);
 assert(in&&in->kind==Keycap::Spec::Mouse&&in->button==6);
 assert(out&&out->kind==Keycap::Spec::Mouse&&out->button==7);
 // Trigger icons remain mapped to combat bindings outside the editor.
 state.preview=false;
 assert(IconSpec(kL2,state)->labels[0]==std::to_string(kBindFirearm));
 assert(IconSpec(kR2,state)->labels[0]==std::to_string(kBindStrong));
 // Shoulder icons must not be mistaken for the editor's zoom triggers.
 state.preview=true;
 assert(IconSpec(kL1,state)->labels[0]==std::to_string(kBindTransform));
 assert(IconSpec(kR1,state)->labels[0]==std::to_string(kBindAttack));
 // Explicit gamepad mode retains its original controller icon path.
 state.keys=false;state.xbox=true;
 assert(IconSpec(kL2,state)->kind==Keycap::Spec::Pad);
 state.xbox=false;assert(!IconSpec(kL2,state));
 std::puts("PASS: production preview prompts use WheelUp/WheelDown for L2/R2; gameplay bindings and controller icons remain contextual.");
}
'''
with tempfile.TemporaryDirectory(prefix='appearance-prompts-') as temp:
    root=Path(temp);cpp=root/'prompts.cpp';cpp.write_text(program)
    subprocess.run([a.cxx,'-std=c++23','-I'+str(engine/'gpu/shim'),str(cpp),'-o',str(root/'prompts')],check=True)
    subprocess.run([str(root/'prompts')],check=True)
