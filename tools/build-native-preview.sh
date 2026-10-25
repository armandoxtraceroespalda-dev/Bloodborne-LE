#!/usr/bin/env bash
set -euo pipefail
# Compatibility alias: there is now one launcher, installed in LauncherApp.
exec bash "$(dirname -- "${BASH_SOURCE[0]}")/build-launcher-only.sh" "$@"
