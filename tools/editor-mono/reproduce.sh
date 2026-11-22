#!/bin/sh
set -eu
recipe_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(CDPATH= cd -- "$recipe_dir/../.." && pwd)
[ "$#" = 1 ] || { echo "Usage: $0 NEW_OWNED_OUTPUT_DIRECTORY" >&2; exit 2; }
[ "$(git -C "$repo_dir" branch --show-current)" != main ] || { echo 'Run this opt-in diagnostic in an owned code worktree.' >&2; exit 2; }
output=$(python3 -c 'from pathlib import Path;import sys;print(Path(sys.argv[1]).resolve())' "$1")
[ ! -e "$output" ] || { echo 'Output already exists; preserve it and choose a new directory.' >&2; exit 2; }
destination="$repo_dir/unity/Assets/Spacewars/Tests/PlayMode/MonoAllocationContentionTests.cs"
[ ! -e "$destination" ] && [ ! -e "$destination.meta" ] || { echo 'Diagnostic source already exists; refusing to overwrite it.' >&2; exit 2; }
mkdir -p "$output"
cp "$recipe_dir/MonoAllocationContentionTests.cs" "$destination"
trap 'rm -f "$destination" "$destination.meta"' EXIT
export SPACEWARS_MONO_OUTPUT="$output"
SPACEWARS_EXPECT_MONO_TAG=${SPACEWARS_EXPECT_MONO_TAG:-9d7c9afd-pthread}
export SPACEWARS_EXPECT_MONO_TAG
"$repo_dir/tools/unity.sh" exclusive -batchmode -noaudio -muted -runTests -testPlatform PlayMode -testFilter 'Spacewars.Tests.PlayMode.MonoAllocationContentionTests.BackgroundAllocatorSerialization;Spacewars.Tests.PlayMode.MonoAllocationContentionTests.RuntimeIdentityMatchesRequestedEngine' -testResults "$output/results.xml" -logFile "$output/editor.log" > "$output/host.log" 2>&1
