// SPDX-License-Identifier: GPL-3.0-or-later
#include "cinematic_voice.h"
#include <cassert>
#include <iostream>
#include <string>
#include <vector>
using CinematicVoice::Track;
int main(int argc,char** argv) {
    if(argc>2) {
        std::vector<Track> tracks;
        for(int i=2;i<argc;++i) {
            const std::string_view item(argv[i]);const auto a=item.find('|'),b=item.find('|',a+1);
            assert(a!=std::string_view::npos && b!=std::string_view::npos);
            tracks.push_back({std::stoi(std::string(item.substr(0,a))),item.substr(a+1,b-a-1),item.substr(b+1)});
        }
        const auto result=CinematicVoice::Select(tracks,CinematicVoice::Parse(argv[1]),1);
        std::cout<<result.stream<<" "<<result.reason<<"\n";return 0;
    }
    const std::array tracks={Track{1,"jpn","Japanese"},Track{2,"eng","English"},Track{3,"fra","French"},
        Track{4,"ita","Italian"},Track{5,"deu","German"},Track{6,"spa","Spanish (Spain)"},
        Track{7,"spa","Spanish (Latin America)"},Track{8,"por","Portuguese (Brazil)"}};
    assert(CinematicVoice::Select(tracks,1,6).stream==2);
    assert(CinematicVoice::Select(tracks,20,6).stream==7);
    assert(CinematicVoice::Select(tracks,3,7).stream==6);
    assert(CinematicVoice::Select(tracks,0,6).stream==1);
    assert(CinematicVoice::Select(tracks,2,6).stream==3);
    assert(CinematicVoice::Select(tracks,4,6).stream==5);
    assert(CinematicVoice::Select(tracks,5,6).stream==4);
    assert(CinematicVoice::Select(tracks,17,6).stream==8);
    auto gpac=tracks;
    for(auto& track:gpac){track.language="und";track.title="GPAC ISO Audio Handler";}
    assert(CinematicVoice::Select(gpac,1,6).stream==2);
    assert(CinematicVoice::Select(gpac,20,6).stream==7);
    assert(CinematicVoice::Select(gpac,3,7).stream==6);
    auto conflicting=gpac;conflicting[0].language="eng";
    assert(CinematicVoice::Select(conflicting,20,6).stream==-1);
    assert(CinematicVoice::Select(std::span<const Track>(gpac).first(7),20,6).stream==-1);
    auto unlabeled=tracks;unlabeled[5].title="";unlabeled[6].title="";
    assert(CinematicVoice::Select(unlabeled,20,6).stream==7);
    assert(CinematicVoice::Select(unlabeled,3,7).stream==6);
    const std::array reduced={Track{4,"eng",""},Track{19,"spa",""},Track{23,"es-MX",""}};
    assert(CinematicVoice::Select(reduced,20,19).stream==23);
    const std::array uncertain={Track{4,"eng",""},Track{19,"spa",""},Track{23,"spa",""}};
    assert(CinematicVoice::Select(uncertain,20,19).stream==-1);
    assert(CinematicVoice::Select(uncertain,20,19).reason=="ambiguous");
    const std::array missing={Track{2,"eng",""},Track{4,"es-ES",""}};
    assert(CinematicVoice::Select(missing,20,4).stream==-1);
    assert(CinematicVoice::Select(missing,20,4).reason=="unavailable");
    assert(CinematicVoice::Select(missing,1,4).stream==2);
    assert(CinematicVoice::Select(missing,-1,4).stream==-1);
    assert(CinematicVoice::Parse(nullptr)==-1 && CinematicVoice::Parse("")==-1 && CinematicVoice::Parse("20x")==-1);
    assert(CinematicVoice::Parse("8")==-1 && CinematicVoice::Parse("20")==20);
    std::cout<<"PASS cinematic voice selection, regional metadata, supported layout and safe fallback\n";
}
