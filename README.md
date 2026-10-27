<img width="1280" height="800" alt="image" src="https://github.com/user-attachments/assets/90e3e411-d94c-40fd-9c3a-bc28cdc00c73" />

# Lantern Engine
<a href="https://trendshift.io/repositories/302591?utm_source=trendshift-badge&amp;utm_medium=badge&amp;utm_campaign=badge-trendshift-302591" target="_blank" rel="noopener noreferrer"><img src="https://trendshift.io/api/badge/trendshift/repositories/302591/daily?language=C%23" alt="Awakenchan%2FBloodBorneButPc | Trendshift" width="250" height="55"/></a>

Lantern Engine runs your own copy of Bloodborne on Windows and Linux: a launcher and my engine, built on bbport and shadPS4 and reworked by me for performance. The game's own code runs natively on your CPU; its graphics are translated to Vulkan.

**No game files are included.** You need your own legal copy of Bloodborne (CUSA03173) with update 1.09, dumped from your own PS4.

### This build is a customized fork of [Lantern Engine](https://github.com/Awakenchan/LanternEngine) by [Awakenchan](https://github.com/Awakenchan), retaining its engine and upstream performance improvements while adding a different launcher and expanded controls.

Launcher

- Rebuilt using native Win32, Direct2D and DirectWrite instead of WPF, averaging approximately 12 MB of RAM versus 160 MB for the original launcher in our testing. Usage varies by system and activity.
- Bloodborne-inspired interface with responsive layouts, animated transitions, game sounds and multilingual support.
- Global settings search, gamepad navigation, reorganized settings and customized achievement notifications.

Controls and in-game experience

- Dark Souls 3–style keyboard/mouse controls with combination bindings.
- Configurable movement turning and smoother sprint direction changes.
- WASD movement while navigating the inventory with the mouse.
- Much smoother mouse-driven character rotation in character creation and the appearance editor, alongside wheel zoom and expanded slider interaction.
- Customized in-game menu organization and localization.

## Credits

| | |
|---|---|
| **Lantern Engine** | by **Awaken**: the launcher, the built-in game patches, and the engine reworked from its starting point (native shader compiler, direct draw calls, draw pipeline and memory performance work, PC menus and controls, upscaler integration, cheats, occlusion culling, trophies, fixes) |
| Engine starting point | [DarkIzuku/bloodborne_pc](https://github.com/DarkIzuku/bloodborne_pc) (built on [bbport](https://github.com/deadinside28/bloodborne_pc) and [shadPS4](https://github.com/shadps4-emu/shadPS4)) |
| Renderer crash fixes | adapted from [AwesomeObserver](https://github.com/AwesomeObserver/bloodborne_pc) and [deadinside28](https://github.com/deadinside28/bloodborne_pc) |
| Online backend for The Hunter's Dream | [droogie/bbhost](https://github.com/droogie/bbhost) |
| Online servers | [The Hunter's Dream](https://thehuntersdream.com) |
| FSR 3.1 and FSR 4 on Vulkan | [FireBurn/FSR-Vulkan](https://github.com/FireBurn/FSR-Vulkan) and the AMD FidelityFX SDK |
| FSR 4.1.1 | [fsr4vk](https://github.com/dvj5411/fsr4vk), with the AMD driver fix from [0xCydral](https://github.com/0xCydral/bloodborne_pc) |
| DLSS | NVIDIA DLSS SDK |
| Sound | [LibAtrac9](https://github.com/Thealexbarney/LibAtrac9) |
| In-game menu | [Dear ImGui](https://github.com/ocornut/imgui) |
| Controller mappings | [SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB) |
| UI library | [WPF-UI](https://github.com/lepoco/wpfui) |

## License

Lantern Engine's own code, the launcher and the built-in game patches are GPL-3.0-or-later with additional terms (see [LICENSE](LICENSE) and [NOTICE](NOTICE)): anything that uses or ships them must keep the credit "Lantern Engine by Awaken", changed versions must say they are changed and must not pass as an official Lantern Engine release, and the name "Lantern Engine" is not licensed for other projects. Parts by others keep their own licenses, listed in the engine's `licenses/` folder.

Source: the engine's full source is in [`engine/`](engine) (0.9.37 beta 7 on), the launchers' in [`launcher/windows/`](launcher/windows), and the Help & FAQ page in [`faq/`](faq).
