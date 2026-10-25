#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Apply the focused movement upgrade, then use this clone's existing engine builder.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
python3 "$root/tools/apply-movement-options.py" --root "$root"
exec bash "$root/tools/build-better-kbm.sh" "$@"
