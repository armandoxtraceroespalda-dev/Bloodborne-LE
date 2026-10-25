// SPDX-License-Identifier: GPL-3.0-or-later
// Run the ACTUAL pointer/list implementation against synthetic guest objects.
#define TEST_POINTER
#include "../engine/engine_hooks.h"
#include <cstring>
namespace BbEngine {
inline void Discard(Hook&) {}
inline std::uint8_t* TailStub(Callback, std::uint64_t) { return nullptr; }
inline void FreeStub(std::uint8_t*) {}
inline bool WriteCode(void* dst, const void* src, std::size_t size) { std::memcpy(dst,src,size);return true; }
}
#include "menu_pointer.cpp"
using namespace MenuPointer;
using namespace PadBits;
#include <cassert>
#include <vector>
#include <cstdarg>
static std::vector<uint8_t> arena(0x5a00000);
static bool mode=true,movement=false,focus=true,controller=false,arrows=false,decides=false,preview=false,direct_arrows=false,empty_cursor=false;
static float px=10,py=10;
static bool left_held=false;
static int delivered_focus=-1,cursor_sets=0,scrolls=0,value_notifications=0;
static uint64_t base(){return reinterpret_cast<uint64_t>(arena.data());}
static void* at(size_t offset){return arena.data()+offset;}
template<class T>static void put(void* p,size_t o,T v){std::memcpy(static_cast<uint8_t*>(p)+o,&v,sizeof v);}
static uint8_t* component=arena.data()+0x5800000;
static uint64_t makefn=0x410000,boundsfn=0x410100;
static uint8_t* object(int row){return arena.data()+0x5810000+row*0x200;}
static uint8_t* datafor(int row){return arena.data()+0x5820000+row*0x100;}
static void wrapper(void* dst,int row){std::memset(dst,0,0x80);put(dst,0x48,uint32_t(10));put(dst,0x50,reinterpret_cast<uint64_t>(datafor(row)));}
bool test_safe_read(const void* p,void* out,size_t n){auto a=reinterpret_cast<uint64_t>(p);if(a<base()||a-base()>arena.size()||n>arena.size()-(a-base()))return false;std::memcpy(out,p,n);return true;}
int64_t test_guest_call(void* fn,const int64_t* a,size_t){
 const auto off=reinterpret_cast<uint64_t>(fn)-base();
 if(off==makefn){auto packed=*reinterpret_cast<const uint64_t*>(a[2]);wrapper(reinterpret_cast<void*>(a[0]),int(packed>>32));return 0;}
 if(off==kChildClip){
  const auto* name=reinterpret_cast<const char*>(a[2]);
  if(arrows&&!std::strcmp(name,direct_arrows?"Slider/LeftArrow":"Widgets/Slider/LeftArrow")){wrapper(reinterpret_cast<void*>(a[0]),3);return 0;}
  if(arrows&&!std::strcmp(name,direct_arrows?"Slider/RightArrow":"Widgets/Slider/RightArrow")){wrapper(reinterpret_cast<void*>(a[0]),4);return 0;}
  if(std::strcmp(name,"Cursor")){std::memset(reinterpret_cast<void*>(a[0]),0,0x80);return 0;}
  if(empty_cursor){wrapper(reinterpret_cast<void*>(a[0]),5);return 0;}
  auto* src=reinterpret_cast<uint8_t*>(a[1]);uint64_t data=Field<uint64_t>(src,0x50);
  int row=int((data-reinterpret_cast<uint64_t>(datafor(0)))/0x100);wrapper(reinterpret_cast<void*>(a[0]),row);return 0;
 }
 if(off==boundsfn){auto* out=reinterpret_cast<float*>(a[0]);int row=int((a[1]-reinterpret_cast<int64_t>(object(0)))/0x200);if(row>=12){out[0]=1200;out[1]=(row-12)*1000.f+300;out[2]=3000;out[3]=out[1]+300;return 0;}if(row>=6){out[0]=row%2==0?1000:3000;out[1]=((row-6)/2)*1000.f;out[2]=out[0]+200;out[3]=out[1]+900;return 0;}if(row==5){std::fill_n(out,4,0.f);return 0;}if(row>=3){out[0]=row==3?1000:3000;out[1]=0;out[2]=out[0]+200;out[3]=900;}else{out[0]=0;out[1]=row*1000.f;out[2]=4000;out[3]=out[1]+900;}return 0;}
 if(off==kStageMatrix){float m[8]={1,0,0,0,0,1,0,0};std::memcpy(reinterpret_cast<void*>(a[1]),m,sizeof m);return 0;}
 if(off==kSetCursor){put(reinterpret_cast<void*>(a[0]),kCursor,int32_t(a[1]));cursor_sets++;return 1;}
 if(off==kSliderSet){
  auto* row=reinterpret_cast<uint8_t*>(a[0]);
  assert(a[2]==1&&a[3]==0x190&&uint64_t(a[4])==base()+0x493387b);
  put(row,kSliderValue,int32_t(a[1]));++value_notifications;return 0;
 }
 if(off==kScrollBy){scrolls++;return 0;}
 if(off==kHideCursors)return 0;
 if(off==kListUpdate){delivered_focus=*reinterpret_cast<const uint8_t*>(a[1]);return 0;}
 assert(false && "Unexpected guest function");return 0;
}
namespace BbEngine {
namespace ChangeAppearance {bool PreviewShown(){return preview;}}
bool host_read_safe(const void* p,void* out,size_t n){return test_safe_read(p,out,n);}
namespace Options {bool ClickDecides(uint8_t*,int){return decides;}void ListUpdate(uint8_t*,bool){}}
}
InputDevice host_input_device(){return controller?InputDevice::Pad:InputDevice::KeyboardMouse;}
namespace BbInput {
PadState LastPad(){PadState p;if(movement)p.ly=0;return p;}
bool PointerLeftHeld(){return left_held;}
bool KeyboardMoving(){return movement;}bool PointerOnStage(float& x,float& y){x=px;y=py;return focus;}
bool MouseIsMenuPointer(){return mode;}bool TextEntryOpen(){return false;}
}
namespace BbOverlay {bool MenuOpen(){return false;}bool CapturesInput(){return false;}}
void host_log(const char*,...){}
static void pc_list_update(uint8_t* list,uint8_t* focus){
 uint64_t saved[6]{};saved[5]=reinterpret_cast<uint64_t>(list);saved[4]=reinterpret_cast<uint64_t>(focus);
 OnListUpdate(0,saved);
 delivered_focus=*reinterpret_cast<const uint8_t*>(saved[4]);
}
static uint32_t click_button(uint8_t* list,int row,uint64_t cell,float x,float y){return ClickMeaning(list,{row,cell},x,y);}
static int preview_zoom_calls=0;
static float preview_zoom_amount=0;
static void __attribute__((sysv_abi)) preview_zoom_mock(uint64_t model,float amount){
 assert(model>=base()+0x5890000&&model<=base()+0x5893000);
 ++preview_zoom_calls;preview_zoom_amount+=amount;
}
static int preview_rotate_calls=0;
static float preview_yaw=0,preview_pitch=0;
static void __attribute__((sysv_abi)) preview_rotate_mock(uint64_t model,float yaw,float pitch){
 assert(model>=base()+0x5890000&&model<=base()+0x5893000);
 ++preview_rotate_calls;preview_yaw+=yaw;preview_pitch+=pitch;
}
int main(){
 // Native zoom receives signed distance on all four model views, rather
 // than a held trigger that merges rapid detents into a single pulse.
 g_image=arena.data();
 const uint64_t background=base()+0x5880000;
 put(reinterpret_cast<void*>(background),0xae8,int32_t(0));
 put(at(kPreviewRates),0,.02f);
 for(unsigned i=0;i<4;++i)put(reinterpret_cast<void*>(background),0xac8+i*8,base()+0x5890000+i*0x1000);
 assert(ApplyPreviewZoom(background,1,preview_zoom_mock));
 const float single=preview_zoom_amount;
 preview_zoom_calls=0;preview_zoom_amount=0;
 assert(ApplyPreviewZoom(background,8,preview_zoom_mock));
 assert(preview_zoom_calls==4&&std::fabs(preview_zoom_amount-8*single)<1e-6f);
 preview_zoom_amount=0;assert(ApplyPreviewZoom(background,-.5f,preview_zoom_mock));
 assert(std::fabs(preview_zoom_amount+.5f*single)<1e-6f);
 const int calls=preview_zoom_calls;
 assert(!ApplyPreviewZoom(background,0,preview_zoom_mock));
 assert(!ApplyPreviewZoom(background,NAN,preview_zoom_mock));
 put(reinterpret_cast<void*>(background),0xae8,int32_t(-1));
 assert(!ApplyPreviewZoom(background,1,preview_zoom_mock));
 put(reinterpret_cast<void*>(background),0xae8,int32_t(0));
 put(reinterpret_cast<void*>(background),0xac8+3*8,uint64_t(0));
 assert(!ApplyPreviewZoom(background,1,preview_zoom_mock));assert(preview_zoom_calls==calls);
 g_preview_zoom_hook=true;assert(ZoomPreview(8));assert(g_preview_wheel.Take()==8);
 assert(g_preview_wheel.Take()==0);ZoomPreview(4);ClearPreviewZoom();assert(g_preview_wheel.Take()==0);
 uint64_t preview_saved[6]{};preview_saved[5]=background;
 ZoomPreview(4);preview=false;assert(OnPreviewUpdate(0,preview_saved)==0);
 assert(g_preview_wheel.Take()==0);g_preview_zoom_hook=false;
 std::puts("PASS: native preview receives 8x distance for 8 detents, signed fractional motion, one call per view, and rejects invalid/inactive state.");
 // Proportional rotation sends both axes in the native float ABI and preserves
 // the game's yaw/pitch signs and per-mode rates, including tiny diagonal input.
 put(reinterpret_cast<void*>(background),0xac8+3*8,base()+0x5893000);
 put(at(kPreviewRates),4,.04f);put(at(kPreviewRates),8,.03f);
 assert(ApplyPreviewRotation(background,{.01f,.02f},preview_rotate_mock));
 assert(preview_rotate_calls==4&&preview_yaw>0&&preview_pitch>0);
 assert(std::fabs(preview_yaw-.0012f)<1e-7f&&std::fabs(preview_pitch-.0032f)<1e-7f);
 preview_rotate_calls=0;preview_yaw=preview_pitch=0;
 assert(ApplyPreviewRotation(background,{-1.f,-2.f},preview_rotate_mock));
 assert(preview_rotate_calls==4&&preview_yaw<0&&preview_pitch<0);
 assert(!ApplyPreviewRotation(background,{},preview_rotate_mock));
 assert(!ApplyPreviewRotation(background,{NAN,1},preview_rotate_mock));
 put(reinterpret_cast<void*>(background),0xae8,int32_t(-1));
 assert(!ApplyPreviewRotation(background,{1,1},preview_rotate_mock));
 put(reinterpret_cast<void*>(background),0xae8,int32_t(0));
 g_preview_rotation_hook=true;
 assert(RotatePreview(.25f,0,12.5f));assert(RotatePreview(0,.5f,12.5f));
 auto motion=g_preview_drag.Take();assert(motion.x>0&&motion.y>motion.x);
 assert(g_preview_drag.Take().x==0);RotatePreview(1,1,12.5f);ClearPreviewRotation();
 assert(g_preview_drag.Take().y==0);
 RotatePreview(1,1,12.5f);preview=false;OnPreviewUpdate(0,preview_saved);
 assert(g_preview_drag.Take().x==0);g_preview_rotation_hook=false;
 std::puts("PASS: native drag preserves both float axes and tiny motion, correct signs and preview rates; reset and inactive editor discard stale input.");
 // Reproduce the image+guest-VA layout used by the ORIGINAL loader.
 g_image=arena.data();
 const uint64_t flow=base()+0x5830000;put(at(kMenuFlow),0,flow);
 put(reinterpret_cast<void*>(flow),kFlowStarted,uint8_t(0));put(reinterpret_cast<void*>(flow),kFlowMenu,uint64_t(1));
 put(at(kRegion),0,int32_t(1));std::memcpy(at(kCursorClipName),"Cursor",7);
 put(component,kItems,int32_t(3));put(component,kCursor,int32_t(0));put(component,kCursorOverride,int32_t(-1));
 put(component,kCells,int32_t(1));put(component,kLines,int32_t(3));
 const uint64_t provider=base()+0x5840000,vt=base()+0x5841000;
 put(component,kProvider,provider);put(reinterpret_cast<void*>(provider),0,vt);put(reinterpret_cast<void*>(vt),0x18,base()+makefn);
 const uint64_t objvt=base()+0x5842000;put(reinterpret_cast<void*>(objvt),kBoundsSlot,base()+boundsfn);
 for(uint64_t off:{makefn,boundsfn,kStageMatrix,kChildClip,kSetCursor,kScrollBy,kHideCursors,kListUpdate}){uint8_t pro[]={0x55,0x48,0x89,0xe5};std::memcpy(at(off),pro,4);}
 for(int i=0;i<15;i++){put(object(i),0,objvt);put(object(i),kFlagsHigh,uint8_t(kVisible));put(datafor(i),kDisplayObject,reinterpret_cast<uint64_t>(object(i)));}
 // Selector ticks share the native queue and respect louder pending sounds.
 auto* sound=static_cast<int32_t*>(at(kMenuSound));
 sound[0]=-1;sound[1]=-1;menu_pointer_selection_sound();
 assert(sound[0]==0&&sound[1]==0);
 assert(Field<uint64_t>(static_cast<uint8_t*>(at(kMenuSoundLabel)),0)==base()+kCursorMoveLabel);
 sound[0]=3;sound[1]=1;menu_pointer_selection_sound();
 assert(sound[0]==3&&sound[1]==1);
 uint8_t active=1;
 movement=true;pc_list_update(component,&active);assert(delivered_focus==1); // TITLE keeps WASD.
 put(reinterpret_cast<void*>(flow),kFlowStarted,uint8_t(1));
 py=60;pc_list_update(component,&active);assert(delivered_focus==0);assert(CursorOf(component)==1);assert(cursor_sets==1); // WORLD can move AND hover.
 MenuPointer::Click();pc_list_update(component,&active);assert(MenuPointer::TakePress()==0x4000); // LMB selects actual hit item.
 assert(menu_back_button()==0x2000);put(at(kRegion),0,int32_t(5));assert(menu_back_button()==0x4000); // Region aware back.
 controller=true;pc_list_update(component,&active);assert(delivered_focus==1);controller=false; // Controller keeps list navigation.
 mode=false;BbSettings::Get().mouse_menu=false;pc_list_update(component,&active);assert(delivered_focus==1); // Disabled KBM leaves input intact.
 mode=true;BbSettings::Get().mouse_menu=true;movement=false;pc_list_update(component,&active);assert(delivered_focus==1); // Native pad navigation unaffected.
 put(reinterpret_cast<void*>(flow),kFlowMenu,uint64_t(0));g_focus_ms=NowMs()-35;assert(!menu_pointer_in_menu());
 put(reinterpret_cast<void*>(flow),kFlowStarted,uint8_t(0));assert(menu_pointer_in_menu());
 // Category arrows reach the actual left/right geometry. Action rows retain capture.
 arrows=true;assert(click_button(component,0,0,55,10)==0x80u);
 assert(click_button(component,0,0,155,10)==0x20u);
 decides=true;assert(click_button(component,1,0,55,10)==menu_confirm_button());
 decides=false;arrows=false;
 // The editor owns the pointer with no front-end flow or focused list.
 put(reinterpret_cast<void*>(flow),kFlowStarted,uint8_t(1));
 put(reinterpret_cast<void*>(flow),kFlowMenu,uint64_t(0));g_focus_ms=NowMs()-1000;
 preview=true;movement=true;
 assert(menu_pointer_in_menu()&&MenuPointer::Navigating());
 pc_list_update(component,&active);assert(delivered_focus==1);
 // Creator pages with an empty Cursor still select the visible row by click.
 movement=false;empty_cursor=true;px=10;py=110;
 assert(ItemUnder(component,px,py,false).index==2);
 MenuPointer::Click();pc_list_update(component,&active);
 assert(CursorOf(component)==2&&MenuPointer::TakePress()==menu_confirm_button());
 // Direct Slider children use their own left/right arrow geometry.
 arrows=true;direct_arrows=true;
 assert(click_button(component,0,0,55,10)==kLeft);
 assert(click_button(component,0,0,155,10)==kRight);
 // Arrow input survives multiple pad reads and is cancelled outside the editor.
 g_editor_clicks.Reset();g_press=kRight;assert(MenuPointer::TakePress()==kRight);
 assert(MenuPointer::TakePress()==kRight);
 MenuPointer::CancelClicks();assert(MenuPointer::TakePress()==0);
 g_press=kLeft;assert(MenuPointer::TakePress()==kLeft);
 preview=false;assert(MenuPointer::TakePress()==0);
 empty_cursor=false;arrows=direct_arrows=false;movement=false;
 // The native multi-slider never calls the generic list update. Exercise its
 // actual dialog/vector/wrapper layout and the separate navigation call hook.
 for (const bool embedded : {false, true}) {
 preview=true;mode=true;movement=false;value_notifications=0;MenuPointer::CancelClicks();
 const auto dialog=base()+0x5850000, rows=base()+0x5852000, rowvt=base()+0x5843000;
 put(reinterpret_cast<void*>(rowvt),0x18,base()+kSliderUpdate);
 put(reinterpret_cast<void*>(dialog),kMultiBegin,rows);
 put(reinterpret_cast<void*>(dialog),kMultiEnd,rows+3*8);
 put(reinterpret_cast<void*>(dialog),kMultiCursor,int32_t(0));
 for(int i=0;i<3;++i){
  const auto row=base()+0x5860000+i*0x2000;
  put(reinterpret_cast<void*>(rows),i*8,row);
  put(reinterpret_cast<void*>(row),0,embedded?base()+kAppearanceRowVtable:rowvt);
  if(embedded){
   // Real appearance rows use base child-stack update and embed Slider at +0xa70.
   put(at(kAppearanceRowVtable),0x18,base()+0x1ad5b10);
   put(reinterpret_cast<void*>(row+kEmbeddedSlider),0,rowvt);
  }
  const auto value_row=row+(embedded?kEmbeddedSlider:0);
  wrapper(reinterpret_cast<void*>(row+kDialogRoot),i);
  if(embedded)wrapper(reinterpret_cast<void*>(value_row+kDialogRoot),i);
  wrapper(reinterpret_cast<void*>(value_row+kSliderBar),12+i);
  wrapper(reinterpret_cast<void*>(value_row+kSliderLeft),6+i*2);
  wrapper(reinterpret_cast<void*>(value_row+kSliderRight),7+i*2);
  put(reinterpret_cast<void*>(value_row),kSliderMin,int32_t(0));
  put(reinterpret_cast<void*>(value_row),kSliderMax,int32_t(255));
  put(reinterpret_cast<void*>(value_row),kSliderValue,int32_t(100));
 }
 const auto select=[&](){
  uint64_t start[6]{};start[5]=dialog;start[4]=reinterpret_cast<uint64_t>(&active);
  OnMultiUpdate(0,start);
  uint64_t storage[7]{};auto* read=storage+1;read[5]=reinterpret_cast<uint64_t>(&active);
  const auto handled=OnMultiDirection(0,read);if(handled)assert(read[-1]==0);
  return handled;
 };
 px=10;py=60;MenuPointer::Click();assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(dialog),kMultiCursor)==1);
 const auto row=base()+0x5860000+0x2000+(embedded?kEmbeddedSlider:0);
 put(object(9),kFlagsHigh,uint8_t(0)); // Focus-only arrow may be hidden on the native row.
 px=155;MenuPointer::Click();assert(select()==1);
 put(object(9),kFlagsHigh,uint8_t(kVisible));
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==101&&value_notifications==1);
 assert(select()==0); // A held mouse button does not produce another unit.
 px=55;MenuPointer::Click();assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==100&&value_notifications==2);
 put(reinterpret_cast<void*>(row),kSliderValue,int32_t(0));
 MenuPointer::Click();assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==0);
 px=155;put(reinterpret_cast<void*>(row),kSliderValue,int32_t(255));
 MenuPointer::Click();assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==255);
 // Click a track to select that row and set its absolute value. Capture
 // remains on this row when dragging over another row or past either end.
 left_held=true;px=105;py=75;MenuPointer::Click();assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==128);
 const int after_click=value_notifications;
 assert(select()==1);assert(value_notifications==after_click); // Stationary cursor sends no duplicate change.
 px=150;py=110;assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==255);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(dialog),kMultiCursor)==1);
 px=20;assert(select()==1);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==0);
 left_held=false;px=105;select();
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==0);
 // A fresh click cancels previous capture even if release/repress occurred
 // between native menu frames; cancel/focus state cannot retain drag ownership.
 left_held=true;px=105;py=75;MenuPointer::Click();assert(select()==1);
 MenuPointer::CancelClicks();px=130;select();
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==128);
 left_held=false;select();
 // Standalone appearance sliders use the same native row update and setter,
 // without relying on any MultiSlider parent or generic list callback.
 uint64_t single[6]{};single[5]=row;single[4]=reinterpret_cast<uint64_t>(&active);
 px=155;py=60;put(reinterpret_cast<void*>(row),kSliderValue,int32_t(100));
 const int before=value_notifications;MenuPointer::Click();
 assert(OnSliderUpdate(0,single)==0);assert(OnSliderUpdate(0,single)==0);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==101);
 assert(value_notifications==before+1);
 // Standalone rows also support direct track dragging without a parent.
 left_held=true;px=105;py=75;MenuPointer::Click();OnSliderUpdate(0,single);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==128);
 px=200;OnSliderUpdate(0,single);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==255);
 left_held=false;px=105;OnSliderUpdate(0,single);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(row),kSliderValue)==255);
 mode=false;px=10;py=110;MenuPointer::Click();assert(select()==0);
 assert(Field<int32_t>(reinterpret_cast<uint8_t*>(dialog),kMultiCursor)==1);
 mode=true;active=0;assert(select()==0);active=1;
 MenuPointer::CancelClicks();preview=false;
 }
 std::puts("PASS: native appearance tracks support click-to-position and captured dragging for flat/embedded/standalone sliders, exact arrows, native notification, endpoint clamp and release/cancel.");
 assert(active==1); // Never mutate caller's focus flag or gameplay pad.
 std::puts("PASS: real hit-test/hover/click implementation; title WASD, world movement isolation, KBM-off and region back.");
}
