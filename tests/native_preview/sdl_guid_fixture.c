/* Linux-only SDL ABI fixture. Never used by the installed launcher or engine. */
#include <stdint.h>
#include <stdbool.h>
#include <stdlib.h>
#include <stdio.h>
typedef struct { uint8_t bytes[16]; } SDL_GUID;
static uint32_t menu_buttons; static int16_t menu_x,menu_y,menu_scroll;
void fixture_set_menu(uint32_t buttons,int16_t x,int16_t y,int16_t scroll) { menu_buttons=buttons;menu_x=x;menu_y=y;menu_scroll=scroll; }
bool SDL_SetHint(const char *name,const char *value) { (void)name;(void)value;return true; }
bool SDL_InitSubSystem(uint32_t flags) { (void)flags;return true; }
void SDL_QuitSubSystem(uint32_t flags) { (void)flags; }
void SDL_Quit(void) {}
uint32_t *SDL_GetJoysticks(int *count) { uint32_t *ids=malloc(3*sizeof *ids); ids[0]=1;ids[1]=2;ids[2]=3;*count=3;return ids; }
uint32_t *SDL_GetGamepads(int *count) { uint32_t *ids=malloc(2*sizeof *ids);ids[0]=1;ids[1]=2;*count=2;return ids; }
void SDL_free(void *memory) { free(memory); }
void *SDL_OpenGamepad(uint32_t id) { return (void *)(uintptr_t)id; }
const char *SDL_GetGamepadNameForID(uint32_t id) { return id==1?"DualSense Test":"Xbox Test"; }
const char *SDL_GetJoystickNameForID(uint32_t id) { (void)id;return "Unmapped wheel"; }
bool SDL_IsGamepad(uint32_t id) { return id<3; }
void SDL_UpdateGamepads(void) {}
bool SDL_GetGamepadButton(void *pad,int button) { (void)pad;return (menu_buttons & (1u<<button))!=0; }
int16_t SDL_GetGamepadAxis(void *pad,int axis) { (void)pad;return axis==0?menu_x:axis==1?menu_y:axis==3?menu_scroll:0; }
const char *SDL_GetGamepadStringForButton(int button) { (void)button;return "a"; }
void SDL_CloseGamepad(void *pad) { (void)pad; }
SDL_GUID SDL_GetGamepadGUIDForID(uint32_t id) { SDL_GUID g;for(int i=0;i<16;i++)g.bytes[i]=(uint8_t)(i+(id-1)*16);return g; }
void SDL_GUIDToString(SDL_GUID guid,char *buffer,int size) { if(size<33)return; for(int i=0;i<16;i++)sprintf(buffer+i*2,"%02x",guid.bytes[i]);buffer[32]=0; }
