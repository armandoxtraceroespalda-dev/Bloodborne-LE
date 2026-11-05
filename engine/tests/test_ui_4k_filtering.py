#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Exercise production 4K packing and CPU pixel/sampler policies without a GPU."""
import argparse
import json
import os
from pathlib import Path
import shlex
import subprocess
import tempfile


def function(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sanitize", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    cache = (root / "gpu/shadps4/video_core/texture_cache/texture_cache.cpp").read_text()
    raster = (root / "gpu/shadps4/video_core/renderer_vulkan/vk_rasterizer.cpp").read_text()
    prompts = (root / "gpu/shim/engine/key_prompts.cpp").read_text()
    generated = '''#include <cmath>
#define main Ui4kToolMain
#include TOOL_SOURCE
#undef main
namespace RuntimePixels {
using u8 = std::uint8_t;
PIXEL_FUNCTIONS
}
// Only the GPU-facing value holders are substituted; sampler policy is production code.
namespace AmdGpu {
enum class Filter { Point, Bilinear, AnisoPoint, AnisoLinear };
enum class MipFilter { Point, Linear };
template<class T> struct Field {
    T value{};
    operator T() const { return value; }
    void Assign(T v) { value = v; }
};
struct Sampler {
    Field<Filter> xy_min_filter, xy_mag_filter;
    Field<MipFilter> mip_filter;
    Field<int> min_lod, max_lod;
};
}
namespace VideoCore {
struct TextureCache {
    static inline bool loaded = false;
    static bool UiReplacementsLoaded() { return loaded; }
};
}
struct Rasterizer {
    bool ui_movie_draw = false, ui_additive = false;
    float SmoothUiSampler(AmdGpu::Sampler&, bool, bool) const;
};
SAMPLER_FUNCTION
namespace KeyPrompts {
namespace {
enum Icon { kCircle, kCross, kL3 };
HUD_METADATA
}
HUD_FUNCTIONS
}
'''.replace("TOOL_SOURCE", json.dumps((root / "tools/engine/ui_4k.cpp").as_posix()))
    generated = generated.replace("PIXEL_FUNCTIONS", function(cache, "void Halve(") + "\n" + function(cache, "static void UpscaleRect("))
    generated = generated.replace("SAMPLER_FUNCTION", function(raster, "float Rasterizer::SmoothUiSampler("))
    generated = generated.replace("HUD_METADATA", prompts[prompts.index("constexpr int kHudBlockY"):prompts.index("std::uint64_t HudHash(")])
    generated = generated.replace("HUD_FUNCTIONS", "\n".join(function(prompts, signature) for signature in ("void DecodeHud(", "std::array<HudRect, 3> HudPrompts(", "std::uint64_t HudHashes(")))
    with tempfile.TemporaryDirectory(prefix="bb-ui-filtering-") as work:
        work = Path(work)
        (work / "ui_filtering_under_test.h").write_text(generated)
        exe = work / ("ui-filtering-test.exe" if os.name == "nt" else "ui-filtering-test")
        command = shlex.split(os.environ.get("CXX", "c++"))
        command += ["-std=c++20", "-O1" if args.sanitize else "-O2", "-g", "-UNDEBUG", "-Wall", "-Wextra", "-Werror", "-pthread"]
        if args.sanitize:
            command += ["-fsanitize=address,undefined", "-fno-omit-frame-pointer"]
        command += shlex.split(os.environ.get("CPPFLAGS", "")) + shlex.split(os.environ.get("CXXFLAGS", ""))
        command += ["-I", str(work), "-I", str(root / "gpu/shim"), str(root / "tests/test_ui_4k_filtering.cpp"), str(root / "gpu/shim/hud_gauge.cpp")]
        command += shlex.split(os.environ.get("LDFLAGS", "")) + ["-lxxhash", "-lz", "-o", str(exe)]
        subprocess.run(command, check=True)
        env = dict(os.environ)
        env.pop("BB_UI_SMOOTH", None)
        env.pop("BB_UI_LOD_BIAS", None)
        subprocess.run([str(exe), str(work / "assets")], check=True, env=env)
        subprocess.run([str(exe), "--sampler-off"], check=True, env={**env, "BB_UI_SMOOTH": "0"})
        for value, expected in [("-9", "-2"), ("9", "0"), ("-1.25", "-1.25")]:
            subprocess.run([str(exe), "--bias", expected], check=True, env={**env, "BB_UI_LOD_BIAS": value})
    print("4K UI filtering/packing regression tests: PASS")


if __name__ == "__main__":
    main()
