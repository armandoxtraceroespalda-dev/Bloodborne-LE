#!/usr/bin/env python3
"""Apply the cinematic-only voice override without replacing an engine integration."""
from pathlib import Path
import hashlib
import json
import os
import sys

SOURCE='gpu/shadps4/core/libraries/avplayer/avplayer_source.cpp'
HEADER='gpu/shadps4/core/libraries/avplayer/cinematic_voice.h'
MARKER='// BetterKBM: select cinematic audio independently of text/subtitles.'
PREVIOUS_HEADER='25075afbbaf1f1a854e40cb0f2cb12fa55c44405b9ac52abd4f3f8bebc5c399d'

def digest(data):
    return hashlib.sha256(data.replace(b'\r\n',b'\n')).hexdigest()

def replace_once(text,old,new):
    if text.count(old)!=1:
        raise RuntimeError('Unexpected AvPlayer source layout; no files were changed. Review this upstream update before applying the cinematic patch.')
    return text.replace(old,new,1)

def patched_source(text):
    if MARKER in text:return text
    text=replace_once(text,'#include "core/libraries/avplayer/avplayer_source.h"',
        '#include "core/libraries/avplayer/avplayer_source.h"\n#include "core/libraries/avplayer/cinematic_voice.h"\n#include <cstdlib>\n#include <cstdio>')
    text=replace_once(text,'    case AVMediaType::AVMEDIA_TYPE_AUDIO: {\n        m_audio_stream_index = stream_index;',
'''    case AVMediaType::AVMEDIA_TYPE_AUDIO: {
        // BetterKBM: select cinematic audio independently of text/subtitles.
        const int requested_voice = CinematicVoice::Parse(std::getenv("BB_VOICE_LANGUAGE"));
        if (requested_voice >= 0) {
            std::vector<CinematicVoice::Track> tracks;
            tracks.reserve(m_streams.size());
            for (const auto& entry : m_streams) {
                const auto candidate = m_avformat_context->streams[entry.ffmpeg_index];
                if (candidate->codecpar->codec_type != AVMEDIA_TYPE_AUDIO) continue;
                const auto language = av_dict_get(candidate->metadata, "language", nullptr, 0);
                auto title = av_dict_get(candidate->metadata, "title", nullptr, 0);
                if (!title) title = av_dict_get(candidate->metadata, "handler_name", nullptr, 0);
                tracks.push_back({static_cast<int>(entry.ffmpeg_index), language ? language->value : "",
                                  title ? title->value : ""});
            }
            const auto choice = CinematicVoice::Select(tracks, requested_voice, static_cast<int>(stream_index));
            if (choice.stream >= 0) stream_index = static_cast<u32>(choice.stream);
            std::printf("Movie voice: requested=%d selected stream=%u (%.*s)\\n", requested_voice,
                        stream_index, static_cast<int>(choice.reason.size()), choice.reason.data());
            const char* trace = std::getenv("BB_AUDIO_TRACE");
            if (choice.stream < 0 || (trace && trace[0] == '1')) {
                for (const auto& track : tracks)
                    std::printf("Movie audio stream %d: language=%.*s title=%.*s\\n", track.stream,
                                static_cast<int>(track.language.size()), track.language.empty() ? "" : track.language.data(),
                                static_cast<int>(track.title.size()), track.title.empty() ? "" : track.title.data());
            }
        }
        m_audio_stream_index = stream_index;''')
    # EnableStream stores FFmpeg stream IDs. Decoder/timekeeping accesses must
    # not index the filtered supported-stream vector with those physical IDs.
    old_video='m_streams[m_video_stream_index.value()].ffmpeg_index'
    old_audio='m_streams[m_audio_stream_index.value()].ffmpeg_index'
    if text.count(old_video)!=3 or text.count(old_audio)!=3:
        raise RuntimeError('Unexpected AvPlayer stream indexing; no files were changed.')
    text=text.replace(old_video,'m_video_stream_index.value()').replace(old_audio,'m_audio_stream_index.value()')
    text=replace_once(text,'const auto index = m_streams[stream_index.value()].ffmpeg_index;',
                          'const auto index = static_cast<u32>(stream_index.value());')
    text=replace_once(text,'if (index >= m_streams.size()) {','if (index >= m_avformat_context->nb_streams) {')
    return text

def atomic_write(path,data):
    temporary=path.with_name(path.name+'.cinematic.tmp')
    try:
        temporary.write_bytes(data);os.replace(temporary,path)
    finally:temporary.unlink(missing_ok=True)

def apply(root):
    root=Path(root);manifest_path=root/'better-kbm-manifest.json'
    manifest_data=manifest_path.read_bytes();manifest=json.loads(manifest_data)
    source=root/SOURCE;original=source.read_bytes()
    if digest(original)!=manifest['files'][SOURCE]['after']:
        raise RuntimeError('AvPlayer source does not match the current integration manifest. Keep your local changes and review them before patching.')
    header=root/HEADER
    if not header.is_file():raise RuntimeError('Missing cinematic_voice.h; copy every file from ARCHIVOS first.')
    if (HEADER in manifest['files'] and digest(header.read_bytes())!=manifest['files'][HEADER]['after']
        and manifest['files'][HEADER]['after']!=PREVIOUS_HEADER):
        raise RuntimeError('Cinematic voice header differs from the current manifest; no files were changed.')
    updated=patched_source(original.decode('utf-8-sig').replace('\r\n','\n')).encode('utf-8')
    if manifest.get('cinematic_voice_patch')==2 and digest(updated)==digest(original):
        print('Cinematic voice override already applied.');return
    manifest['files'][SOURCE]['after']=digest(updated)
    manifest['files'][HEADER]={'before':None,'after':digest(header.read_bytes())}
    manifest['cinematic_voice_patch']=2
    manifest['version']=int(manifest['version'])+1
    new_manifest=(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n').encode('utf-8')
    try:
        atomic_write(source,updated);atomic_write(manifest_path,new_manifest)
    except Exception:
        atomic_write(source,original);atomic_write(manifest_path,manifest_data);raise
    print('Cinematic voice override applied; integration',manifest['version'],'upstream',manifest['launcher_source_revision'])

if __name__=='__main__':
    try:
        if len(sys.argv)!=2:raise RuntimeError('Usage: apply-cinematic-voice.py ENGINE_FOLDER')
        apply(sys.argv[1])
    except (OSError,ValueError,KeyError,RuntimeError) as error:raise SystemExit(str(error))
