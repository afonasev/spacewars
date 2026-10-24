#!/bin/sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
project_dir="$repo_dir/unity"
runner=${UNITY_RUNNER:-$HOME/.local/bin/unity-run}
editor=${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity}
[ -x "$runner" ] || { echo "Install ~/.local/share/unity-run/install.sh first" >&2; exit 2; }
# Couple the application and engine quiet contracts before starting either runtime.
quiet=0
has_engine_quiet=0
has_app_quiet=0
for argument in "$@"; do
  case "$argument" in
    -noaudio) quiet=1; has_engine_quiet=1 ;;
    -muted) quiet=1; has_app_quiet=1 ;;
    -batchmode) quiet=1 ;;
  esac
done
if [ "$quiet" = 1 ]; then
  [ "$has_engine_quiet" = 1 ] || set -- "$@" -noaudio
  [ "$has_app_quiet" = 1 ] || set -- "$@" -muted
fi
case "${1:-}" in
  shared|exclusive)
    mode=$1; shift
    [ -d "$project_dir" ] || { echo "Missing Unity project: $project_dir" >&2; exit 2; }
    exec "$runner" "--$mode" --project "$project_dir" -- "$editor" -projectPath "$project_dir" "$@" ;;
  player)
    shift
    mode=${UNITY_RUN_MODE:-shared}
    [ "$mode" = shared ] || [ "$mode" = exclusive ] || exit 2
    [ "$#" -gt 0 ] || { echo "Provide direct Player executable" >&2; exit 2; }
    exec "$runner" "--$mode" -- "$@" ;;
  *) echo "Usage: tools/unity.sh shared|exclusive [Editor arguments] | player EXECUTABLE [arguments]" >&2; exit 2 ;;
esac
