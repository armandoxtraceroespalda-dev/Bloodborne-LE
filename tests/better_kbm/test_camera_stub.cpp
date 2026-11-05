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
static void aim_camera_test(){
 using namespace MouseCamera;
 std::array<unsigned char,0x200> camera{};
 std::array<unsigned char,0x60> chr{};
 std::array<unsigned char,0x288> input{};
 std::array<unsigned char,0x48> stick{};
 const auto link=[](auto& object,std::size_t at,const auto& target){
  const auto* ptr=target.data();
  std::memcpy(object.data()+at,&ptr,sizeof ptr);
 };
 link(chr,0x58,input);link(input,0x80,stick);
 const std::uint64_t saved[6]={0,0,0,0,reinterpret_cast<std::uint64_t>(chr.data()),
                             reinterpret_cast<std::uint64_t>(camera.data())};
 camera[0x188]=1;
 write(camera.data(),0xf0,30);write(camera.data(),0xf4,-25);
 Publish(5,false,false,false,true);
 AddCounts(10,20);
 OnAimUpdate(0,saved);
 assert(std::abs(read(camera.data(),0x148)-0.034906585f)<1e-6f);
 assert(std::abs(read(camera.data(),0x14c)-0.017453292f)<1e-6f);
 const auto initial=camera;
 OnAimUpdate(0,saved);assert(camera==initial); // No replay.
 write(stick.data(),0x40,.5f);
 link(input,0x280,stick); // The game's override takes priority over its base input.
 AddCounts(50,60);OnAimUpdate(0,saved);assert(camera==initial);
 write(stick.data(),0x40,0);
 Publish(5,true,true,false,true);
 AddCounts(10,1000);OnAimUpdate(0,saved);
 assert(std::abs(read(camera.data(),0x148)+0.43633231f)<1e-6f);
 assert(std::abs(read(camera.data(),0x14c))<1e-6f);
 g_shown_until=Clock::now()+std::chrono::seconds(1);
 float x,y;assert(!Flick(x,y)); // No lock-on flick in the monocular.
 Publish(5,false,false,false,false);
 const auto disabled=camera;
 AddCounts(20,30);OnAimUpdate(0,saved);assert(camera==disabled);
 OnCameraUpdate(0,nullptr);
 assert(!g_aiming && g_update_x==0 && g_update_y==0);
 std::puts("PASS: aiming-camera angles, inversion, native limit, stick priority, and no replay/flick.");
}
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
 aim_camera_test();
}

// The online party tracker is external to this camera-code fixture.
namespace BbEngine::OnlineFixes { void SetCameraYaw(float) {} }
