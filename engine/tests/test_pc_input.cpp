// SPDX-License-Identifier: GPL-3.0-or-later
// The integrated binding table and mouse policy; full event coverage lives in tests/better_kbm.
#include "../gpu/shim/input/bindings.h"
#include "../gpu/shim/input/mouse_turn.h"
#include "../gpu/shim/bbport_settings.h"
#include <SDL3/SDL.h>
#include <cassert>
#include <cmath>
#include <cstdio>
void host_log(const char*,...){}
bool debug_menu_active(){return false;}
std::uint32_t menu_confirm_button(){return 0x4000;}
std::uint32_t menu_back_button(){return 0x2000;}
namespace BbSettings {Values& Get(){static Values s;return s;}}
int main(){
 static_assert(kBindCount==31&&kBindPages==6&&kBindPerPage==6);
 host_bindings_load_defaults();
 assert(host_binding_key(kBindMoveF)==SDL_GetScancodeFromName("W"));
 assert(host_binding_key(kBindWalk)==SDL_SCANCODE_LALT);
 assert(host_binding_mouse(kBindAttack)==1);
 assert(host_binding_ini_value(kBindDpadUp)=="T");
 assert(host_bindings_parse("strong_attack","Left Shift + Mouse1"));
 assert(host_binding_ini_value(kBindStrong)=="Left Shift + Mouse1");
 assert(host_bindings_parse("switch_right","Left Shift + WheelUp"));
 assert(host_binding_ini_value(kBindSwitchRight)=="Left Shift + WheelUp");
 assert(std::abs(MouseTurn::DegreesPerCount(5)-.1f)<1e-6f);
 const MouseTurn::Settings s{MouseTurn::DegreesPerCount(5),false,false};
 auto turn=[&](int frames){float pitch=0,yaw=0;for(int i=0;i<frames;++i)
     MouseTurn::Turn(120.f/frames,60.f/frames,s,pitch,yaw,-1,1);return std::pair{pitch,yaw};};
 const auto a=turn(30),b=turn(60),c=turn(240);
 assert(std::abs(a.first-b.first)<1e-5f&&std::abs(a.second-c.second)<1e-5f);
 std::puts("PC input: PASS (custom action inventory, T, wheel chords and frame-independent mouse gain)");
}
