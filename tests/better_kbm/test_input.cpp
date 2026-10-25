// SPDX-License-Identifier: GPL-3.0-or-later
// The actual input bridge and binding implementation; SDL/renderer services are mocked.
#include "input.cpp"
#include <cassert>
#include <iostream>
#include <thread>
static bool test_menu=false,test_front=false,test_navigating=false,test_overlay=false,test_capture=false,test_preview=false,test_reset_preview=false;
namespace BbOverlay { bool MenuOpen(){return test_overlay;} bool CapturesInput(){return test_overlay;} }
namespace BbEngine::Options {bool ClickDecides(uint8_t*,int){return false;} void ListUpdate(uint8_t*,bool){} }
static bool native_zoom=false,native_drag=false,native_sliders=false;
static float native_dx=0,native_dy=0;
static float native_wheel=0;
static bool published_mouse=false,published_hold=false,flick_enabled=false;
namespace MouseCamera {
bool Install(uint8_t*,size_t){return true;} bool Installed(){return true;}
void Reset(){} void AddCounts(float,float){}
void Publish(int,bool,bool,bool hold,bool enabled){published_mouse=enabled;published_hold=hold;}
bool Flick(float& x,float& y){x=1;y=0;return flick_enabled;}
}
namespace MenuPointer {
bool Install(uint8_t*,size_t){return true;}
bool Navigating(){return test_navigating;} bool FrontEnd(){return test_front;}
void Click(){test_capture=true;} uint32_t TakePress(){if(test_reset_preview){test_reset_preview=false;return 4;}return test_capture?(test_capture=false,0x4000):0;}
bool NativeSliders(){return native_sliders;}
void CancelSliderDrag(){}
void HoldRow(bool){}
void CancelClicks(){test_capture=false;test_reset_preview=false;}
void ResetPreview(){test_reset_preview=true;}
bool ZoomPreview(float wheel){if(!native_zoom)return false;native_wheel+=wheel;return true;}
void ClearPreviewZoom(){native_wheel=0;}
bool RotatePreview(float dx,float dy,float){if(!native_drag)return false;native_dx+=dx;native_dy+=dy;return true;}
void ClearPreviewRotation(){native_dx=native_dy=0;}
}
namespace BbEngine::ChangeAppearance {bool PreviewShown(){return test_preview;}}
bool menu_pointer_in_menu(){return test_menu;}
uint32_t menu_confirm_button(){return 0x4000;} uint32_t menu_back_button(){return 0x2000;}
namespace BbEngine::ItemCycle {bool Install(uint8_t*,size_t){return true;}}
static bool reverse_request=false;
void native_item_cycle_request(bool reverse){reverse_request=reverse;}void native_item_cycle_cancel(){reverse_request=false;}
void menu_pointer_scroll(int){}
extern "C" uint64_t runtime_pad_bindings_serial(){return 0;}
extern "C" int runtime_pad_export_binding(int,char*,size_t,char*,size_t){return 0;}
extern "C" int runtime_pad_button(uint32_t,int){return -1;}
extern "C" void runtime_pad_request_binding(uint32_t,int,int){}
static bool keys[512]{};
static void key(const char* n,bool down=true){auto sc=SDL_GetScancodeFromName(n);host_bindings_key_event(sc,down);keys[sc]=down;}
static BbPcPad sample(bool gamepad=false,BbPcPad p={}){bbgpu_pc_input(&p,keys,512,gamepad);return p;}
static void clear(){for(int i=0;i<512;i++)if(keys[i])host_bindings_key_event(i,false);std::fill_n(keys,512,false);g_mouse.held=g_mouse.pressed=0;g_mouse.wheel=0;}
int main(){
 g_active=true;host_bindings_load_defaults();
 assert(host_binding_ini_value(kBindDpadUp)=="T");
 // Explicit saved/custom bindings still override defaults.
 host_bindings_parse("dpad_up","Y");assert(host_binding_ini_value(kBindDpadUp)=="Y");
 BbSettings::ApplyDs3Profile();
 auto& settings=BbSettings::Get();settings.mouse_menu=true;
 key("W");key("Space");auto p=sample();assert(p.ly==0 && (p.buttons&0x2000));
 key("A");p=sample();assert(p.lx<=127 && p.ly==0 && (p.buttons&0x2000));
 test_menu=test_navigating=true;
 // WASD moves the player without scrolling or backing out of the inventory.
 p=sample();assert(p.lx==0&&p.ly==0&&!(p.buttons&0xf0)&&!(p.buttons&0x2000));assert(host_keyboard_moving());
 key("Up");p=sample();assert((p.buttons&0x10)&&p.lx==0&&p.ly==0);key("Up",false);
 // Controller mode preserves physical input and keyboard fallback.
 BbSettings::SelectKeyboardMouse(false);
 BbPcPad physical{};physical.buttons=0x2000;physical.lx=200;physical.ly=127;
 clear();p=sample(true,physical);assert((p.buttons&0x2000)&&p.lx==200&&!host_keyboard_moving());
 BbSettings::SelectKeyboardMouse(true);
 fixture_sdl_ticks+=300; // The preceding Back transition has settled.
 key("W");test_front=true;p=sample();assert((p.buttons&0x10)&&p.ly==127&&!host_keyboard_moving());
 test_front=false;settings.mouse_menu=false;p=sample();assert((p.buttons&0x10)&&p.ly==127);
 settings.mouse_menu=true;clear();key("Backspace");p=sample();assert(p.buttons&0x2000);
 clear();g_mouse.pressed=SDL_BUTTON_RMASK;p=sample();assert(p.buttons&0x2000);
 clear();g_mouse.pressed=1;p=sample();assert(p.buttons&0x4000);
 clear();key("Escape");p=sample();assert((p.buttons&8)&&!(p.buttons&0x2000));
 test_menu=test_navigating=false;
 clear();key("E");p=sample();assert((p.buttons&0x4000)&&!(p.buttons&0x8000));
 clear();key("R");p=sample();assert((p.buttons&0x8000)&&!(p.buttons&0x4000));
 clear();key("C");p=sample();assert(p.buttons&0x1000);
 clear();g_mouse.held=1;p=sample();assert((p.buttons&0x800)&&!(p.buttons&0x200));
 key("Left Shift");p=sample();assert((p.buttons&0x200)&&p.r2==255&&!(p.buttons&0x800));
 key("Left Shift",false);p=sample();assert(!(p.buttons&0xa00));
 clear();sample();g_mouse.held=SDL_BUTTON_RMASK;p=sample();assert((p.buttons&0x100)&&p.l2==255&&!(p.buttons&0x400));
 clear();sample();key("Left Ctrl");p=sample();assert((p.buttons&0x100)&&!(p.buttons&2));
 clear();g_mouse.wheel=-1;p=sample();assert(p.buttons&0x40);
 clear();sample();g_mouse.wheel=1;p=sample();assert((p.buttons&0x40)&&reverse_request);
 clear();sample();reverse_request=false;key("Left Shift");g_mouse.wheel=1;p=sample();assert((p.buttons&0x20)&&!(p.buttons&0x40)&&!reverse_request);
 clear();sample();key("Left Shift");g_mouse.wheel=-1;p=sample();assert((p.buttons&0x80)&&!(p.buttons&0x40)&&!reverse_request);
 clear();sample();key("Down");p=sample();assert(!(p.buttons&0x40));
 clear();sample();
 // Resolve real SDL g_mouse.wheel events while Shift is still held, before the next pad read.
 clear();sample();g_weapon_wheel.Reset();reverse_request=false;
 SDL_Event shift_event{};shift_event.type=SDL_EVENT_KEY_DOWN;shift_event.key.scancode=SDL_GetScancodeFromName("Left Shift");
 BbInput::Event(shift_event,false);
 SDL_Event wheel_event{};wheel_event.type=SDL_EVENT_MOUSE_WHEEL;wheel_event.wheel.y=1;
 BbInput::Event(wheel_event,false);shift_event.type=SDL_EVENT_KEY_UP;BbInput::Event(shift_event,false);
 p=sample();assert((p.buttons&0x20)&&!(p.buttons&0xc0)&&!reverse_request);
 for(int poll=0;poll<20;++poll){p=sample();assert((p.buttons&0x20)&&!(p.buttons&0xc0));}
 g_weapon_wheel.Reset();shift_event.type=SDL_EVENT_KEY_DOWN;BbInput::Event(shift_event,false);
 wheel_event.wheel.y=-1;BbInput::Event(wheel_event,false);
 shift_event.type=SDL_EVENT_KEY_UP;BbInput::Event(shift_event,false);
 p=sample();assert((p.buttons&0x80)&&!(p.buttons&0x60));
 test_menu=test_navigating=true;p=sample();assert(!(p.buttons&0xa0)&&g_weapon_wheel.pending.empty());
 test_menu=test_navigating=false;clear();sample();
 // The DS3 profile uses T for Blood Bullets without binding the title arrows.
 clear();key("T");p=sample();assert(p.buttons&0x10u);
 clear();sample();
 // Upstream multi-input alternatives coexist with the release-to-finish chord workflow.
 const auto before_firearm=host_binding_ini_value(kBindFirearm);
 assert(host_bindings_parse("firearm","X, Left Shift + Mouse3"));
 clear();sample();key("X");p=sample();assert(p.buttons&0x100);
 clear();sample();key("Left Shift");g_mouse.held=SDL_BUTTON_RMASK;p=sample();assert(p.buttons&0x100);
 clear();sample();g_mouse.held=SDL_BUTTON_RMASK;p=sample();assert(!(p.buttons&0x100));
 clear();sample();assert(host_bindings_parse("firearm",before_firearm.c_str()));
 // Chord capture accumulates until all constituent inputs have been released.
 host_bind_capture_begin(kBindFirearm);clear();sample();std::this_thread::sleep_for(std::chrono::milliseconds(310));
 host_bind_capture_key(SDL_GetScancodeFromName("Left Shift"));key("Left Shift");host_bind_capture_mouse(3);g_mouse.held=SDL_BUTTON_RMASK;sample();
 assert(host_bind_capturing()==kBindFirearm);key("Left Shift",false);sample();assert(host_bind_capturing()==kBindFirearm);
 g_mouse.held=0;sample();assert(host_bind_capturing()==-1);assert(host_bind_capture_take_done()==kBindFirearm);
 assert(host_binding_ini_value(kBindFirearm).find(" + ")!=std::string::npos);
 clear();sample();key("Left Shift");g_mouse.held=SDL_BUTTON_RMASK;p=sample();assert(p.buttons&0x100);
 clear();sample();g_mouse.held=SDL_BUTTON_RMASK;p=sample();assert(!(p.buttons&0x100));
 clear();sample();BbSettings::SelectKeyboardMouse(false);key("E");p=sample();assert((p.buttons&0x4000)&&settings.pc_controls&&!settings.mouse_camera);
 BbSettings::SelectKeyboardMouse(true);assert(settings.mouse_camera);
 // Rollover reversals must keep sprint held and choose the latest direction.
 clear();test_menu=test_navigating=false;key("Space");key("A");p=sample();assert(p.lx==0);
 key("D");p=sample();assert(p.lx==254&&(p.buttons&0x2000));
 // Repeated older key events must not steal direction back.
 key("A");p=sample();assert(p.lx==254);
 key("D",false);p=sample();assert(p.lx==0&&(p.buttons&0x2000));
 clear();sample();key("Space");key("W");p=sample();assert(p.ly==0);
 key("S");p=sample();assert(p.ly==254&&(p.buttons&0x2000));
 // Both edges between pad reads preserve SDL event order.
 clear();sample();key("D");key("A");p=sample();assert(p.lx==0);
 clear();sample();key("A");key("D");p=sample();assert(p.lx==254);
 // Adding a diagonal keeps sprint; there is no direction interpolation.
 clear();sample();key("Space");key("W");sample();key("A");p=sample();assert(p.lx<=127&&p.ly==0&&(p.buttons&0x2000));
 // Controller axes have priority over synthetic mouse target-switch input.
 clear();sample();g_device=InputDevice::Pad;BbSettings::SelectKeyboardMouse(false);flick_enabled=true;
 for(int x=0;x<256;++x)for(int y=0;y<256;++y){
  BbPcPad pad{};pad.rx=x;pad.ry=y;pad.lx=pad.ly=128;
  p=sample(true,pad);assert(p.rx==x&&p.ry==y&&!published_mouse&&!published_hold);
 }
 BbSettings::SelectKeyboardMouse(true);g_device=InputDevice::KeyboardMouse;flick_enabled=false;
 BbPcPad pad{};pad.rx=190;pad.ry=170;
 pad.buttons=0xffff;pad.lx=0;pad.ly=254;pad.l2=pad.r2=255;pad.touch_count=1;
 p=sample(true,pad);assert(p.rx==127&&p.ry==127&&p.lx==127&&p.ly==127&&!p.buttons&&!p.l2&&!p.r2&&!p.touch_count&&published_mouse);
 flick_enabled=false;clear();sample();assert(published_mouse);
 pad.rx=140;pad.ry=128;p=sample(true,pad);assert(published_mouse&&p.rx==127&&p.ry==127);
 SDL_Event pad_event{};pad_event.type=SDL_EVENT_GAMEPAD_AXIS_MOTION;pad_event.gaxis.value=30000;
 BbInput::Event(pad_event,false);assert(g_device==InputDevice::KeyboardMouse);
 std::cout<<"PASS: ordered A/D and W/S rollover, sprint/diagonals, repeat filtering, 65,536 controller axis pairs and mouse gating.\n";
 // Focus loss, text entry and capture cannot leave movement isolation stuck on.
 test_menu=test_navigating=true;clear();key("W");sample();assert(host_keyboard_moving());
 SDL_Event e{};e.type=SDL_EVENT_WINDOW_FOCUS_LOST;BbInput::Event(e,false);assert(!host_keyboard_moving());
 p=sample();assert(p.ly==127&&!p.buttons);
 e.type=SDL_EVENT_WINDOW_FOCUS_GAINED;BbInput::Event(e,false);g_text_entry=true;p=sample();assert(!host_keyboard_moving());
 g_text_entry=false;host_bind_capture_begin(kBindAttack);p=sample();assert(!host_keyboard_moving());
 host_bind_capture_key(SDL_SCANCODE_ESCAPE);host_bind_capture_note_held(false);
 // Editor ownership must survive missing list focus (preview and nested sliders).
 clear();sample();test_preview=true;test_menu=test_front=test_navigating=false;
 g_text_entry=false;test_overlay=false;
 fixture_sdl_ticks+=300;
 g_mouse.held=g_mouse.pressed=SDL_BUTTON_RMASK;p=sample();
 assert((p.buttons&menu_back_button())&&!(p.buttons&0x100)&&p.l2==0&&!published_mouse);
 // Editor zoom bypasses DS3 weapon chords, including when Shift is held.
 clear();sample();SDL_Event editor_shift{};editor_shift.type=SDL_EVENT_KEY_DOWN;
 editor_shift.key.scancode=SDL_GetScancodeFromName("Left Shift");BbInput::Event(editor_shift,false);key("Left Shift");
 SDL_Event editor_wheel{};editor_wheel.type=SDL_EVENT_MOUSE_WHEEL;editor_wheel.wheel.y=1;
 BbInput::Event(editor_wheel,false);p=sample();
 assert((p.buttons&kL2)&&p.l2==255&&!(p.buttons&(kR1|kL1|kRight|kR2))&&!reverse_request&&g_weapon_wheel.pending.empty());
 for(int ms=1;ms<35;++ms){fixture_sdl_ticks++;p=sample();assert((p.buttons&kL2)&&p.l2==255);}
 editor_shift.type=SDL_EVENT_KEY_UP;BbInput::Event(editor_shift,false);
 clear();g_mouse.wheel=-1;p=sample();assert((p.buttons&kR2)&&p.r2==255&&!(p.buttons&(kL2|kR1|kL1)));
 fixture_sdl_ticks+=121;clear();p=sample();assert(!(p.buttons&(kL2|kR2))&&p.l2==0&&p.r2==0);
 // High-resolution wheels accumulate fractional detents.
 for(int n=0;n<3;++n){g_mouse.wheel=.25f;p=sample();assert(!(p.buttons&(kL2|kR2)));}
 g_mouse.wheel=.25f;p=sample();assert((p.buttons&kL2)&&p.l2==255);
 // Text entry cancels queued zoom; it cannot leak into gameplay.
 g_text_entry=true;p=sample();assert(!p.buttons&&g_preview_zoom.direction==0);
 g_text_entry=false;clear();p=sample();assert(!(p.buttons&(kL2|kR2)));
 // Fast SDL wheel bursts reach native zoom with their full magnitude, and
 // never also generate a simulated L2/R2 hold or gameplay wheel action.
 native_zoom=true;native_wheel=0;
 editor_wheel.wheel.y=1;
 for(int n=0;n<8;++n)BbInput::Event(editor_wheel,false);
 p=sample();assert(native_wheel==8&&!(p.buttons&(kL2|kR2|kL1|kR1)));
 native_wheel=0;editor_wheel.wheel.y=-7;BbInput::Event(editor_wheel,false);
 p=sample();assert(native_wheel==-7&&!(p.buttons&(kL2|kR2)));
 native_wheel=0;editor_wheel.wheel.y=.25f;BbInput::Event(editor_wheel,false);
 p=sample();assert(native_wheel==.25f);sample();assert(native_wheel==.25f);
 g_text_entry=true;p=sample();assert(native_wheel==0);g_text_entry=false;
 native_zoom=false;clear();sample();
 // Middle mouse resets the preview through native R3, without a combat binding.
 clear();g_mouse.held=g_mouse.pressed=1u<<1;p=sample();assert(p.buttons&kR3);
 clear();p=sample();assert(!(p.buttons&kR3));
 // WASD navigates the editor even when a world player exists at the mirror.
 clear();sample();fixture_sdl_ticks+=300;key("W");p=sample();
 assert((p.buttons&kUp)&&p.lx==kStickRest&&p.ly==kStickRest&&!host_keyboard_moving());
 // The preview viewport receives rotation, not a list confirmation.
 clear();sample();g_mouse.x=1200;g_mouse.y=200;g_mouse.drag_x=4;g_mouse.drag_y=-2;
 g_mouse.held=g_mouse.pressed=SDL_BUTTON_LMASK;p=sample();
 assert(p.rx>kStickRest&&p.ry<kStickRest&&!(p.buttons&menu_confirm_button()));
 // The game's preview tests a right-stick component against +/-0.2.
 // A one-pixel event must cross that threshold and survive intervening 1 kHz reads.
 clear();sample();g_mouse.x=1200;g_mouse.y=200;g_mouse.drag_x=1;
 g_mouse.held=g_mouse.pressed=SDL_BUTTON_LMASK;p=sample();
 assert((int(p.rx)-int(kStickRest))/127.f>.2f);
 for(int ms=1;ms<40;++ms){fixture_sdl_ticks++;p=sample();assert((int(p.rx)-int(kStickRest))/127.f>.2f);}
 clear();p=sample();assert(p.rx==kStickRest&&p.ry==kStickRest);
 // Native drag receives alternating, fractional axes without publishing a
 // fake right stick. Releasing LMB cancels motion without affecting zoom.
 clear();sample();native_drag=true;
 g_mouse.x=1500;g_mouse.y=500;g_mouse.held=g_mouse.pressed=SDL_BUTTON_LMASK;
 g_mouse.drag_x=.25f;g_mouse.drag_y=0;p=sample();
 assert(native_dx==.25f&&native_dy==0&&p.rx==127&&p.ry==127);
 g_mouse.drag_x=0;g_mouse.drag_y=.5f;p=sample();
 assert(native_dx==.25f&&native_dy==.5f&&p.rx==127&&p.ry==127);
 p=sample();assert(native_dx==.25f&&native_dy==.5f);
 g_mouse.held=0;p=sample();assert(native_dx==0&&native_dy==0);
 native_drag=false;clear();sample();
 // Slider drag outside the preview makes a direction pulse and stops on release.
 clear();sample();g_mouse.x=200;g_mouse.y=200;g_mouse.held=g_mouse.pressed=SDL_BUTTON_LMASK;sample();
 g_mouse.pressed=0;g_mouse.x+=40;p=sample();assert(p.buttons&kRight);
 // The native UI may sample only every 33 ms while the bridge is read every millisecond.
 for(int ms=1;ms<40;++ms){fixture_sdl_ticks++;p=sample();assert(p.buttons&kRight);}
 g_mouse.held=0;p=sample();assert(!(p.buttons&(kRight|kLeft)));
 // Once the native slider hook is installed, slider drags never also
 // publish held Left/Right pulses into the selected appearance row.
 clear();sample();native_sliders=true;
 g_mouse.x=200;g_mouse.y=200;g_mouse.held=g_mouse.pressed=SDL_BUTTON_LMASK;sample();
 g_mouse.x+=120;p=sample();assert(!(p.buttons&(kRight|kLeft)));
 assert(!g_dragging_value&&g_drag_steps.pending==0);
 assert(BbInput::PointerLeftHeld());g_mouse.held=0;assert(!BbInput::PointerLeftHeld());
 native_sliders=false;clear();sample();
 // Closing the editor in the middle of a drag also releases the pointer's row hold.
 g_mouse.held=g_mouse.pressed=SDL_BUTTON_LMASK;sample();g_mouse.pressed=0;g_mouse.x+=80;sample();
 assert(g_dragging_value);test_preview=false,test_reset_preview=false;p=sample();
 assert(g_preview_zoom.direction==0);
 assert(!g_dragging_value&&g_drag_steps.pending==0&&g_drag_steps.held==0&&!(p.buttons&(kRight|kLeft)));
 settings.pc_controls=false;p=sample();assert(!host_keyboard_moving());
 std::cout<<"PASS: actual input bridge: DS3 profile, modifier attacks, separate items/interact, diagonal sprint, inventory WASD, Space, title, arrows, controller, mouse clicks, Escape and focus/capture gates.\n";
}
