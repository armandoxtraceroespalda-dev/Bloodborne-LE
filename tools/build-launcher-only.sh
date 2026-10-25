#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
dotnet_command="$(command -v dotnet.exe || command -v dotnet || true)"
if [[ -z "$dotnet_command" && -f '/c/Program Files/dotnet/dotnet.exe' ]]; then dotnet_command='/c/Program Files/dotnet/dotnet.exe'; fi
[[ -n "$dotnet_command" ]] || { echo 'Install a Windows .NET SDK (8 or later).' >&2; exit 1; }
command -v python3 >/dev/null || { echo 'python3 is required.' >&2; exit 1; }
staging="$(mktemp -d)"
trap 'rm -rf -- "$staging"' EXIT
project="$root/launcher/windows/BloodborneLauncher.Native/BloodborneLauncher.Native.csproj"
"$dotnet_command" publish "$project" -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$staging"
python3 tools/install-primary-launcher.py --published "$staging" --destination "$root/LauncherApp" --previous-preview "$root/NativeLauncherApp"
python3 tools/audit-native-upstream.py --output "$root/LauncherApp/upstream-audit.json"
printf '\nOpen LauncherApp/Bloodborne Launcher.exe. The native launcher is now the default.\n'
