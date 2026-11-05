// SPDX-License-Identifier: GPL-3.0-or-later
// Compiled by test_ui_4k_filtering.py against production functions and packer.
#include "ui_filtering_under_test.h"
#include <cassert>

using AmdGpu::Filter;
using AmdGpu::MipFilter;

void Pixels() {
    int w = 2, h = 2;
    Bytes alpha{255,0,0,255, 0,0,0,0, 0,0,0,0, 0,0,0,0};
    RuntimePixels::Halve(alpha, w, h, false);
    assert(w == 1 && h == 1 && alpha == Bytes({255,0,0,64}));
    Bytes linear{0,0,0,255, 255,255,255,255, 0,0,0,255, 255,255,255,255};
    Bytes srgb = linear;
    w = h = 2;
    RuntimePixels::Halve(linear, w, h, false);
    w = h = 2;
    RuntimePixels::Halve(srgb, w, h, true);
    assert(linear[0] == 128 && srgb[0] >= 187 && srgb[0] <= 188);
    Bytes transparent{20,40,60,0, 20,40,60,0, 20,40,60,0, 20,40,60,0};
    w = h = 2;
    RuntimePixels::Halve(transparent, w, h, false);
    assert(transparent == Bytes({20,40,60,0}));
    Bytes strip{10,20,30,255, 50,60,70,255};
    w = 1; h = 2;
    RuntimePixels::Halve(strip, w, h, false);
    assert(w == 1 && h == 1 && strip == Bytes({30,40,50,255}));

    Bytes prompt{255,0,0,255, 0,0,0,0, 0,0,0,0, 0,0,255,255};
    Bytes target(8 * 8 * 4, 19);
    RuntimePixels::UpscaleRect(prompt, 2, 0, 0, 2, 2, target, 8, 2);
    assert(target[0] == 255 && target[3] == 255);
    assert(target[(3 * 8 + 3) * 4 + 2] == 255);
    assert(target[(7 * 8 + 7) * 4] == 19);
    // Transparent neighbors cannot turn the red edge black.
    assert(target[4] == 255 && target[7] > 0);
    std::puts("4K pixels: alpha edges, sRGB, thin mips and prompt scaling PASS");
}

void Packing(const fs::path& folder) {
    const auto in = folder / "png", out = folder / "pack";
    fs::create_directories(in);
    const unsigned w = KeyPrompts::kHudWidth, h = KeyPrompts::kHudHeight;
    std::vector<std::uint32_t> texels(std::size_t(w) * h, 0x00242424);
    texels[97 * w + 637] = 0xff1830b0;
    texels[97 * w + 638] = 0xff242424; // opaque gray seam between bar glows
    std::string name = HudClean::ArtName();
    name.replace(name.size() - 5, 5, ".png");
    assert(WritePng(in / name, int(w), int(h), texels));
    assert(Pack(in, out) == 0);
    Bytes original, cleaned;
    std::uint32_t ow = 0, oh = 0, cw = 0, ch = 0;
    assert(ReadBbui(out / HudClean::ArtName(), original, ow, oh));
    assert(ReadBbui(out / HudClean::kArtFile, cleaned, cw, ch));
    assert(ow == w && oh == h && cw == w && ch == h);
    const auto seam = (97 * w + 638) * 4;
    assert(original[seam + 3] == 255 && cleaned[seam + 3] == 0);
    assert(cleaned[seam] == 0xb0 && cleaned[seam + 1] == 0x30 && cleaned[seam + 2] == 0x18);
    assert(cleaned[(97 * w + 637) * 4 + 3] == 255);
    // A stale HUD must not survive a pack without its source art.
    fs::remove(out / HudClean::ArtName());
    assert(PackHud(out) && !fs::exists(out / HudClean::kArtFile));
    // Reject truncated containers rather than treating them as HUD art.
    assert(WriteFile(out / "bad.bbui", Bytes{'B','B','U','I'}));
    assert(!ReadBbui(out / "bad.bbui", cleaned, cw, ch));
    std::puts("4K packer: PNG-to-BBUI, HUD glow cleanup, stale HUD and malformed file PASS");
}

void Samplers() {
    Rasterizer raster;
    AmdGpu::Sampler sampler;
    assert(raster.SmoothUiSampler(sampler, false, false) == 0);
    assert(sampler.xy_min_filter.value == Filter::Point);
    assert(raster.SmoothUiSampler(sampler, true, true) == 0);
    assert(sampler.xy_min_filter.value == Filter::Point);
    sampler.xy_mag_filter.Assign(Filter::AnisoPoint);
    assert(raster.SmoothUiSampler(sampler, false, true) == -0.5f);
    assert(sampler.xy_min_filter.value == Filter::Bilinear);
    assert(sampler.xy_mag_filter.value == Filter::AnisoLinear);
    assert(sampler.mip_filter.value == MipFilter::Linear && sampler.max_lod.value == 0xfff);
    VideoCore::TextureCache::loaded = true;
    raster.ui_movie_draw = true;
    sampler = {};
    assert(raster.SmoothUiSampler(sampler, false, false) == 0);
    assert(sampler.xy_min_filter.value == Filter::Bilinear);
    assert(sampler.mip_filter.value == MipFilter::Point);
    sampler = {};
    raster.ui_additive = true;
    assert(raster.SmoothUiSampler(sampler, false, false) == 0);
    assert(sampler.xy_min_filter.value == Filter::Point);
    std::puts("4K samplers: default sharpness, mip filtering, own textures, additive and depth guards PASS");
}

void Prompts() {
    Bytes sheet(KeyPrompts::kHudBytes, 0);
    std::array<std::uint64_t, 3> before{}, after{};
    const auto masked = KeyPrompts::HudHashes(sheet.data(), before);
    const auto rects = KeyPrompts::HudPrompts();
    for (const auto& rect : rects) {
        assert(rect.x >= 0 && rect.y >= 0 && rect.x + rect.w <= int(KeyPrompts::kHudWidth));
        assert(rect.y + rect.h <= int(KeyPrompts::kHudHeight));
    }
    sheet[KeyPrompts::HudBlock(rects[0].x / 4, rects[0].y / 4)] = 1;
    assert(KeyPrompts::HudHashes(sheet.data(), after) == masked);
    assert(before[0] != after[0] && before[1] == after[1] && before[2] == after[2]);
    sheet[0] = 1;
    assert(KeyPrompts::HudHashes(sheet.data(), after) != masked);
    sheet.assign(sheet.size(), 0);
    const auto output_size = std::size_t(KeyPrompts::kHudWidth) * KeyPrompts::kHudHeight * 4;
    Bytes decoded(output_size + 16, 7);
    KeyPrompts::DecodeHud(sheet.data(), decoded.data());
    assert(decoded[0] != 7);
    assert(std::all_of(decoded.begin() + output_size, decoded.end(), [](auto v) { return v == 7; }));
    std::puts("4K HUD prompts: independent hashes, masked art identity, bounded decode PASS");
}

int main(int argc, char** argv) {
    assert(argc >= 2);
    Rasterizer raster;
    AmdGpu::Sampler sampler;
    if (std::strcmp(argv[1], "--bias") == 0) {
        assert(argc == 3);
        assert(raster.SmoothUiSampler(sampler, false, true) == std::strtof(argv[2], nullptr));
        return 0;
    }
    if (std::strcmp(argv[1], "--sampler-off") == 0) {
        VideoCore::TextureCache::loaded = true;
        raster.ui_movie_draw = true;
        assert(raster.SmoothUiSampler(sampler, false, true) == 0);
        assert(sampler.xy_min_filter.value == Filter::Point);
        return 0;
    }
    Pixels();
    Packing(fs::path(argv[1]));
    Prompts();
    Samplers();
}
