// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <array>
#include <charconv>
#include <span>
#include <string_view>

namespace CinematicVoice {
struct Track { int stream; std::string_view language; std::string_view title; };
struct Selection { int stream=-1; std::string_view reason="unset"; };
inline int Parse(const char* value) {
    if(!value || !*value)return -1;
    std::string_view text(value);int result=-1;
    const auto parsed=std::from_chars(text.data(),text.data()+text.size(),result);
    if(parsed.ec!=std::errc{} || parsed.ptr!=text.data()+text.size())return -1;
    switch(result){case 0:case 1:case 2:case 3:case 4:case 5:case 17:case 20:return result;default:return -1;}
}
struct Token {
    std::array<char,160> data{};std::size_t size=0;
    explicit Token(std::string_view text) {
        for(char raw:text) {
            auto c=static_cast<unsigned char>(raw);
            if(c>='A' && c<='Z')c=static_cast<unsigned char>(c-'A'+'a');
            if(((c>='a' && c<='z') || (c>='0' && c<='9')) && size<data.size())data[size++]=static_cast<char>(c);
        }
    }
    std::string_view View()const{return {data.data(),size};}
};
inline int Family(std::string_view tag) {
    const Token token(tag);const auto t=token.View();
    if(t=="ja" || t=="jpn" || t=="jaj" || t=="jajp" || t=="japanese")return 0;
    if(t=="en" || t=="eng" || t=="enus" || t=="engb" || t=="engus" || t=="enggb" || t=="english")return 1;
    if(t=="fr" || t=="fra" || t=="fre" || t=="frf" || t=="frfr" || t=="frafr" || t=="french")return 2;
    if(t=="it" || t=="ita" || t=="iti" || t=="itit" || t=="itait" || t=="italian")return 3;
    if(t=="de" || t=="deu" || t=="ger" || t=="ded" || t=="dede" || t=="deude" || t=="german")return 4;
    if(t=="es" || t=="spa" || t=="esp" || t=="ese" || t=="eses" || t=="spaes" ||
       t=="esa" || t=="esar" || t=="esmx" || t=="es419" || t=="esl" || t=="spaar" || t=="spanish")return 5;
    if(t=="pt" || t=="por" || t=="ptb" || t=="ptbr" || t=="porbr" || t=="portuguese")return 7;
    return -1;
}
// ISO 'spa' alone does not distinguish the two Spanish voice tracks.
inline int SpanishRegion(const Track& track) {
    const Token language(track.language),title(track.title);const auto tag=language.View(),name=title.View();
    if(tag=="esa" || tag=="esar" || tag=="esmx" || tag=="es419" || tag=="esl" || tag=="spaar")return 6;
    if(tag=="ese" || tag=="eses" || tag=="spaes")return 5;
    for(auto word:{"latin","latino","mexic","argentin","america","419"})if(name.find(word)!=std::string_view::npos)return 6;
    for(auto word:{"castell","castilian","spain","espaa","europe"})if(name.find(word)!=std::string_view::npos)return 5;
    return -1;
}
inline int VoiceIndex(int platform) {
    switch(platform){case 0:return 0;case 1:return 1;case 2:return 2;case 3:return 5;case 4:return 4;case 5:return 3;case 20:return 6;case 17:return 7;default:return -1;}
}
inline Selection Select(std::span<const Track> tracks,int platform,int original) {
    const int desired=VoiceIndex(platform);
    if(desired<0)return {};
    // Bloodborne's GPAC movie has eight untagged audio tracks. The guest
    // selects them using the same native voice order as its sound banks.
    // Require the complete observed layout; do not guess for reduced movies.
    if(tracks.size()==8) {
        bool untagged_gpac=true;
        for(const auto& track:tracks) {
            const Token language(track.language),title(track.title);
            if(language.View()!="und" || title.View()!="gpacisoaudiohandler")untagged_gpac=false;
        }
        if(untagged_gpac)return {tracks[static_cast<std::size_t>(desired)].stream,"native GPAC voice order"};
    }
    int best=-1,best_score=0,matches=0;
    for(const auto& track:tracks) {
        const int family=Family(track.language);
        if(family!=(desired==6?5:desired))continue;
        int score=100;
        if(family==5) {
            const int region=SpanishRegion(track);
            if(region>=0 && region!=desired)continue;
            if(region==desired)score=300;
        }
        if(score>best_score){best=track.stream;best_score=score;matches=1;}
        else if(score==best_score){++matches;if(track.stream==original && desired!=5 && desired!=6)best=original;}
    }
    if(matches==1 && (best_score>100 || (desired!=5 && desired!=6)))return {best,"metadata"};
    if(matches>1 && desired!=5 && desired!=6)return {best,"metadata"};
    // The game's eight-language order is used only for the fully recognized
    // layout. A reduced/unknown regional dump must never be indexed blindly.
    constexpr std::array families={0,1,2,3,4,5,5,7};
    if(tracks.size()==families.size() && (desired==5 || desired==6) && best_score==100) {
        bool standard=true;
        for(std::size_t i=0;i<families.size();++i)if(Family(tracks[i].language)!=families[i])standard=false;
        if(standard)return {tracks[static_cast<std::size_t>(desired)].stream,"eight-language layout"};
    }
    return {-1,matches>0?"ambiguous":"unavailable"};
}
}
