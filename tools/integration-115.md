# Upstream integration 115

Upstream: Awakenchan/LanternEngine at e6b943dd969d99561545315f3d801f10e4a6cf8d
(0.9.39 beta29), compared with 60783baf1fb932f367c5627fb043b711f6a917e1
(0.9.39 beta24.1). Engine manifest integration 53.

The native Direct2D launcher remains the only production launcher. New engine
contracts are consumed through linked Core services, rather than importing WPF
views. Looks live in Graphics; save management stays in Game and the editor in
More. Imported Looks use the upstream bounded data-only parser and cannot carry
control bindings, paths, languages or personal settings. Lighting effects and
visitor access remain opt-in. XeSS uses the upstream checksum-verified SDK fetch
script, with its license notices, and the upstream fallback when unavailable.

The custom native in-game menu builder and its localization remain authoritative.
A separate asset dispatcher delegates wide-movies operations to the updated
upstream serializer. Pre-mounted wide movies are accepted only when their exact
file is in the generated widescreen mount list, retaining later mod precedence.
Independent text/voice/cinematic routing is retained alongside upstream Brazilian
Portuguese support. Existing configuration, saves, extracted gui assets and mods
are never shipped in this source overlay.

Build with `bash tools/build-better-kbm.sh` from CLANG64. The engine target is the
project's own `engine` folder. See the package validation report for checks and
platform limits. No remote Git mutations were made.
