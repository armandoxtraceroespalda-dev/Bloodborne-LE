#include "../../better-engine/src/sys/pad_host.c"
#include <assert.h>
/* Only the used SDL string adapter is mocked; queued remapping is the production code. */
const char* SDL_GetGamepadStringForButton(SDL_GamepadButton b){static const char* names[]={"a","b","x","y","back","guide","start","leftstick","rightstick","leftshoulder","rightshoulder","dpup","dpdown","dpleft","dpright"};return b>=0&&b<15?names[b]:"touchpad";}
int main(void){
 default_bindings();pc_publish();
 int key=bindings[IN_CROSS].keys[0];
 runtime_pad_request_binding(PAD_CROSS,0,SDL_GAMEPAD_BUTTON_EAST);pc_apply_pad();
 assert(bindings[IN_CROSS].pad[0]==SDL_GAMEPAD_BUTTON_EAST);
 assert(bindings[IN_CIRCLE].pad[0]==SDL_GAMEPAD_BUTTON_SOUTH);
 assert(bindings[IN_CROSS].keys[0]==key);
 char name[64],value[256];assert(runtime_pad_export_binding(IN_CROSS,name,sizeof name,value,sizeof value));assert(!strcmp(name,"cross")&&!strcmp(value,"b"));
 runtime_pad_request_binding(0,0,-2);pc_apply_pad();assert(bindings[IN_CROSS].pad[0]==SDL_GAMEPAD_BUTTON_SOUTH&&bindings[IN_CROSS].keys[0]==key);
 for(int i=0;i<=IN_RIGHT;++i)assert(runtime_pad_export_binding(i,name,sizeof name,value,sizeof value));
 bindings[IN_MENU_CONFIRM].pad_count=1;bindings[IN_MENU_CONFIRM].pad[0]=SDL_GAMEPAD_BUTTON_EAST;
 bindings[IN_MENU_BACK].pad_count=1;bindings[IN_MENU_BACK].pad[0]=SDL_GAMEPAD_BUTTON_SOUTH;pc_publish();
 assert(runtime_pad_export_binding(IN_RIGHT+1,name,sizeof name,value,sizeof value));assert(!strcmp(name,"menu_confirm")&&!strcmp(value,"b"));
 assert(runtime_pad_export_binding(IN_RIGHT+2,name,sizeof name,value,sizeof value));assert(!strcmp(name,"menu_back")&&!strcmp(value,"a"));
 runtime_pad_request_binding(0,0,-2);pc_apply_pad();
 assert(runtime_pad_export_binding(IN_RIGHT+1,name,sizeof name,value,sizeof value)&&!value[0]);
 assert(runtime_pad_export_binding(IN_RIGHT+2,name,sizeof name,value,sizeof value)&&!value[0]);
 puts("PASS: production controller queue, conflict swaps, factory reset, independent keyboard map and persisted button names.");
}
