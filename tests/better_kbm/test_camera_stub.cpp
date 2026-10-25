// SPDX-License-Identifier: GPL-3.0-or-later
// Execute the current production camera trampoline, preserving the real stick stores.
#include "mouse_camera.cpp"
#include <array>
#include <cassert>
#include <cstdarg>
void host_log(const char*,...){}
extern "C" __attribute__((sysv_abi)) void drive_camera(unsigned char*,float,float,float,float);
asm(R"(
.text
.globl drive_camera
drive_camera:
 push %rbp
 mov %rsp,%rbp
 push %r13
 sub $1024,%rsp
 mov %rdi,%r13
 vunpcklps %xmm1,%xmm0,%xmm1
 vunpcklps %xmm3,%xmm2,%xmm10
 lea .Lcamera_test_resume(%rip),%rax
 mov %rax,lantern_free_camera_resume(%rip)
 jmp lantern_free_camera_site
.Lcamera_test_resume:
 add $1024,%rsp
 pop %r13
 pop %rbp
 ret
)");
static float read(const unsigned char* c,int at){float f;std::memcpy(&f,c+at,4);return f;}
static void write(unsigned char* c,int at,float f){std::memcpy(c+at,&f,4);}
int main(){
 using namespace MouseCamera;
 assert(std::abs(MouseTurn::DegreesPerCount(5)-.1f)<1e-6f);
 std::array<unsigned char,1024> cam{};
 g_update_x=50;g_update_y=60;g_enabled=false;
 for(int x=0;x<256;++x)for(int y=0;y<256;++y){
  const float pitch=(x-128)/128.f,yaw=(y-128)/128.f;
  drive_camera(cam.data(),pitch,yaw,pitch,yaw);
  assert(read(cam.data(),0x140)==pitch&&read(cam.data(),0x144)==yaw);
 }
 Publish(5,false,false,false,true);g_update_x=10;g_update_y=20;
 write(cam.data(),0x1ec,1);write(cam.data(),0x1f0,-1);
 drive_camera(cam.data(),0,0,0,0);
 assert(read(cam.data(),0x140)>0&&read(cam.data(),0x144)>0);
 // Physical stick input wins even with queued mouse counts and the camera enabled.
 drive_camera(cam.data(),.1f,.2f,.1f,.2f);
 assert(read(cam.data(),0x140)==.1f&&read(cam.data(),0x144)==.2f);
 Publish(5,false,false,false,false);g_update_x=10;g_update_y=20;
 drive_camera(cam.data(),0,0,0,0);
 assert(read(cam.data(),0x140)==0&&read(cam.data(),0x144)==0);
 std::puts("PASS: current native camera trampoline, 65,536 controller pairs, 0.1 gain, mouse gate and stick priority.");
}
