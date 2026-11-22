#!/bin/sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
exec python3 "$repo_dir/tools/editor_mono_runtime.py" launch --editor "${SPACEWARS_BASE_UNITY_EDITOR:?}" -- "$@"
