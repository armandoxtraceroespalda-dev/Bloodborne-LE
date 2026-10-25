#!/usr/bin/env bash
set -euo pipefail
# Compatibility alias: there is now one launcher, installed in the project root with support files in launcher.
exec bash "$(dirname -- "${BASH_SOURCE[0]}")/build-launcher-only.sh" "$@"
