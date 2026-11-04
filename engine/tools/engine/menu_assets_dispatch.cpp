// SPDX-License-Identifier: GPL-3.0-or-later
// Keep the custom vanilla menu layout and route widescreen operations to upstream.
#define main custom_menu_assets_main
#include "menu_assets.cpp"
#undef main
#define main upstream_menu_assets_main
#include "menu_assets_main.cpp"
#undef main
int main(int argc, char** argv) {
    if (argc > 1 && std::string(argv[1]) == "wide-movies") return upstream_menu_assets_main(argc, argv);
    return custom_menu_assets_main(argc, argv);
}
