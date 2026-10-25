// SPDX-License-Identifier: GPL-3.0-or-later
#include "editor_drag.h"
#include <cassert>
#include <cstdio>

int main() {
    for (const int hz : {30, 60, 90, 120, 144, 240}) {
        for (int phase = 0; phase < 34; ++phase) {
            EditorDrag::Steps steps;
            steps.Push(5);
            int pressed = 0, previous = 0, frame = -1;
            for (int ms = 0; ms < 700; ++ms) {
                const int value = steps.Poll(ms); // An unrelated pad read every millisecond.
                const int next_frame = ms < phase ? -1 : ((ms - phase) * hz / 1000);
                if (next_frame == frame) continue;
                frame = next_frame;
                if (value && !previous) ++pressed;
                previous = value;
            }
            assert(pressed == 5);
            steps.Push(-32);steps.Reset();assert(steps.Poll(701) == 0);

            EditorDrag::Rotation rotation;
            frame = -1;
            for (int ms = 0; ms < 300; ++ms) {
                const auto v = rotation.Poll(ms % 8 == 0 ? 1.f : 0.f, 0, ms, 12.5f);
                const int next_frame = ms < phase ? -1 : ((ms - phase) * hz / 1000);
                if (next_frame == frame) continue;
                frame = next_frame;
                assert((int(v.x) - 127) / 127.f > .2f && v.y == 127);
            }
            rotation.Reset();
            const auto released = rotation.Poll(0, 0, 300, 12.5f);
            assert(released.x == 127 && released.y == 127);
        }
    }
    // Three clicks must produce exactly three edges at every 30 Hz frame phase.
    for (int phase=0;phase<34;++phase) {
        EditorDrag::Clicks clicks;
        for(int n=0;n<3;++n) clicks.Push(0x20);
        int edges=0,frame=-1;std::uint32_t previous=0;
        for(int ms=0;ms<400;++ms){
            const auto value=clicks.Poll(ms);
            const int next=ms<phase?-1:(ms-phase)*30/1000;
            if(next==frame)continue;
            frame=next;if(value&&!previous)++edges;previous=value;
        }
        assert(edges==3);
        clicks.Push(0x4000);clicks.Reset();assert(clicks.Poll(401)==0);
    }
    // Native wheel zoom preserves distance, including a burst delivered as one
    // SDL event or many pad reads before the next preview frame.
    EditorDrag::WheelZoom distance;
    distance.Push(8);assert(distance.Take()==8);assert(distance.Take()==0);
    for(int i=0;i<8;++i) distance.Push(1);
    assert(distance.Take()==8);assert(distance.Take()==0);
    distance.Push(.25f);distance.Push(.25f);assert(distance.Take()==.5f);
    distance.Push(3);distance.Push(-1);assert(distance.Take()==2);
    distance.Push(-4);assert(distance.Take()==-4);assert(distance.Take()==0);
    distance.Push(5);distance.Reset();assert(distance.Take()==0);
    distance.Push(NAN);distance.Push(INFINITY);assert(distance.Take()==0);
    for(int hz:{30,60,90,120,144,240}) {
        distance.Reset();float received=0;
        for(int ms=0;ms<200;++ms) {
            if(ms<40) distance.Push(.25f);
            if(ms*hz/1000!=(ms-1)*hz/1000) received+=distance.Take();
        }
        received+=distance.Take();assert(received==10);assert(distance.Take()==0);
    }
    std::puts("PASS: proportional native zoom preserves every detent at 30..240 Hz, including batched and fractional wheel events, with no timed backlog.");
    // Drag displacement is independent of pad polling and frame rate. Alternating
    // X-only and Y-only events retain both axes instead of replacing one another.
    for(int hz:{30,60,90,120,144,240}) {
        EditorDrag::DragRotation drag;
        EditorDrag::Motion total;
        for(int ms=0;ms<200;++ms) {
            if(ms<100)drag.Push(ms%2==0?.25f:0,ms%2==1?.125f:0,12.5f);
            if(ms*hz/1000!=(ms-1)*hz/1000){auto v=drag.Take();total.x+=v.x;total.y+=v.y;}
        }
        auto last=drag.Take();total.x+=last.x;total.y+=last.y;
        assert(std::fabs(total.x-12.5f*12.5f/127)<1e-5f);
        assert(std::fabs(total.y-6.25f*12.5f/127)<1e-5f);
        auto idle=drag.Take();assert(idle.x==0&&idle.y==0);
    }
    EditorDrag::DragRotation drag;
    drag.Push(.01f,-.02f,12.5f);auto tiny=drag.Take();
    assert(tiny.x>0&&tiny.y<0&&std::fabs(tiny.y/tiny.x+2)<1e-6f);
    drag.Push(1,0,12.5f);drag.Push(0,2,12.5f);auto diagonal=drag.Take();
    assert(diagonal.x>0&&std::fabs(diagonal.y/diagonal.x-2)<1e-6f);
    drag.Push(5,7,12.5f);drag.Push(-5,-7,12.5f);auto reverse=drag.Take();
    assert(reverse.x==0&&reverse.y==0);
    drag.Push(1,1,12.5f);drag.Reset();auto cancelled=drag.Take();assert(cancelled.x==0&&cancelled.y==0);
    drag.Push(NAN,1,12.5f);auto finite_axis=drag.Take();assert(finite_axis.x==0&&finite_axis.y>0);
    std::puts("PASS: proportional drag keeps small and diagonal two-axis motion across 30..240 Hz, with no repeated held motion and immediate reset.");
    EditorDrag::Zoom zoom;
    assert(zoom.Poll(1, 0) == 1);
    for(int ms=1;ms<80;++ms) assert(zoom.Poll(0, ms) == 1);
    assert(zoom.Poll(0, 80) == 0);
    assert(zoom.Poll(-1, 81) == -1);
    assert(zoom.Poll(1, 82) == 1); // Reverse immediately, without stale opposite input.
    zoom.Reset();
    for(int n=0;n<3;++n) assert(zoom.Poll(.25f, 200+n) == 0);
    assert(zoom.Poll(.25f, 203) == 1);
    zoom.Reset();assert(zoom.Poll(0, 204) == 0);
    assert(zoom.Poll(1000, 300) == 1);
    assert(zoom.Poll(0, 380) == 0); // Large deltas cannot create a time backlog.
    zoom.Reset();
    for(int ms=0;ms<=200;++ms) assert(zoom.Poll(1, ms) == 1);
    assert(zoom.Poll(0, 279) == 1);
    assert(zoom.Poll(0, 280) == 0); // Even sustained fast scrolling stops after 80 ms.
    assert(zoom.Poll(-1, 281) == -1);
    assert(zoom.Poll(1, 282) == 1);
    assert(zoom.Poll(0, 362) == 0);
    // Every 30 Hz sampling phase must observe a detent despite 1 kHz pad reads.
    for(int phase=0;phase<34;++phase){
        zoom.Reset();bool observed=false;int frame=-1;
        for(int ms=0;ms<120;++ms){
            const int value=zoom.Poll(ms==0?1.f:0.f,ms);
            const int next=ms<phase?-1:(ms-phase)*30/1000;
            if(next!=frame){frame=next;observed|=value!=0;}
            if(ms>=80)assert(value==0);
        }
        assert(observed);
    }
    std::puts("PASS: wheel zoom has no queued time; fast scrolling and large deltas stop within 80 ms, reversal is immediate, fractional detents work, and 30 Hz sampling sees every pulse.");
    EditorDrag::Rotation r;
    auto v = r.Poll(-1, 1, 0, 2.5f);assert(v.x < 127 && v.y > 127);
    v = r.Poll(0, 0, 51, 2.5f);assert(v.x == 127 && v.y == 127);
    std::puts("PASS: editor rotation clears the native 0.2 gate; 1 kHz reads preserve slider edges at 30..240 FPS across 34 phases; release cancels immediately.");
}
