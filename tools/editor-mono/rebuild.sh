#!/bin/sh
# Native runtime source reproduction only. Never builds a Unity Player or runs tests.
set -eu
recipe_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ "${1:-}" != --build-native ]; then
  [ "$#" = 1 ] || { echo "Usage: $0 NEW_OWNED_BUILD_DIRECTORY" >&2; exit 2; }
  [ "$(uname -s)" = Darwin ] && [ "$(uname -m)" = arm64 ] || exit 2
  build_root=$(python3 -c 'from pathlib import Path;import sys;print(Path(sys.argv[1]).resolve())' "$1")
  python3 - "$recipe_dir/runtime.json" "$build_root" <<'PY'
import hashlib,json,subprocess,sys,tarfile
from pathlib import Path
if sys.version_info < (3,12):
    raise RuntimeError('Native source extraction requires Python3.12 or newer')
spec=json.loads(Path(sys.argv[1]).read_text())['sources'];root=Path(sys.argv[2]);root.mkdir(parents=True,exist_ok=False)
for name,owner,repo,archive,target in [
 ('mono','Unity-Technologies','mono','mono.tar.gz','mono'),
 ('bdwgc','Unity-Technologies','bdwgc','bdwgc.tar.gz','mono/external/bdwgc'),
 ('libatomic_ops','Unity-Technologies','libatomic_ops','atomic.tar.gz','mono/external/bdwgc/libatomic_ops'),
 ('corefx','mono','corefx','corefx.tar.gz','mono/external/corefx')]:
    path=root/archive
    subprocess.run(['curl','--fail','--silent','--show-error','--location',f'https://codeload.github.com/{owner}/{repo}/tar.gz/{spec[name]}','-o',str(path)],check=True)
    with path.open('rb') as stream:
        if hashlib.file_digest(stream,'sha256').hexdigest()!=spec['archives'][archive]:
            raise RuntimeError('Pinned source archive hash mismatch: '+archive)
    with tarfile.open(path) as source:
        for member in source:
            relative=member.name.partition('/')[2]
            if relative:source.extract(member.replace(name=relative),root/target,filter='data')
PY
  cd "$build_root/mono"
  patch -p1 < "$recipe_dir/allocator-pthread.patch"
  patch -p1 < "$recipe_dir/macos-sdk.patch"
  exec "${UNITY_RUNNER:-$HOME/.local/bin/unity-run}" --shared -- "$recipe_dir/rebuild.sh" --build-native "$build_root"
fi
[ "$#" = 2 ] || exit 2
build_root=$2
cd "$build_root/mono"
export GIT_CEILING_DIRECTORIES="$build_root"
export MONO_BRANCH=explicit MONO_BUILD_REVISION=9d7c9afd-pthread
export LIBTOOLIZE="$(command -v glibtoolize)"
export CC='clang -arch arm64 -Wno-error=incompatible-function-pointer-types'
export CXX='clang++ -arch arm64'
export CFLAGS='-Os -g -mmacosx-version-min=11.0'
export CXXFLAGS="$CFLAGS" CPPFLAGS='-mmacosx-version-min=11.0'
export SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"
./autogen.sh --disable-mcs-build --disable-support-build --with-glib=embedded --disable-nls --with-mcs-docs=no --prefix="$build_root/install" --enable-no-threads-discovery=yes --enable-ignore-dynamic-loading=yes --enable-dont-register-main-static-data=yes --enable-thread-local-alloc=no --enable-unity-define=yes --with-monotouch=no --host=aarch64-apple-darwinmacos12.2.0 --with-libgdiplus=libgdiplus.dylib --enable-minimal=com,shared_perfcounters --disable-parallel-mark --enable-verify-defines --with-sgen=no --disable-btls --disable-btls-lib > "$build_root/configure.log" 2>&1
make -j4 -C external/bdwgc > "$build_root/build.log" 2>&1
for directory in mono/culture mono/eglib mono/arch mono/utils mono/cil mono/zlib mono/metadata; do
  make -j4 -C "$directory" >> "$build_root/build.log" 2>&1
done
make -C mono/mini version.h cpu-arm64.h >> "$build_root/build.log" 2>&1
make -j4 -C mono/mini libmonoboehm-2.0.la >> "$build_root/build.log" 2>&1
mkdir -p "$build_root/runtime"
cp -L mono/mini/.libs/libmonoboehm-2.0.dylib "$build_root/runtime/libmonobdwgc-2.0.dylib"
codesign --force --sign - --timestamp=none "$build_root/runtime/libmonobdwgc-2.0.dylib"
shasum -a 256 "$build_root/runtime/libmonobdwgc-2.0.dylib"
echo 'Rebuilt candidate only: retain its identity and qualify it before changing runtime.json.'
