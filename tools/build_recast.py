#!/usr/bin/env python3
"""Build the pinned C ABI plugin. No Unity, game or Player launch."""
import argparse
import pathlib
import platform
import shutil
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--build-dir', type=pathlib.Path, default=ROOT / '.local/recast-native')
    p.add_argument('--target', choices=['macos-arm64', 'windows-x64'], default='macos-arm64')
    p.add_argument('--install', action='store_true', help='Copy the plugin into the target Unity Plugins folder')
    a = p.parse_args()
    options = []
    if a.target == 'windows-x64':
        from native_plugins import validate_windows_recast
        name = 'spacewars_recast.dll'
        folder = 'x86_64'
        if platform.system() != 'Windows':
            compiler = shutil.which('x86_64-w64-mingw32-g++')
            if not compiler:
                p.error('Windows cross-build requires x86_64-w64-mingw32-g++')
            options = ['-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_CXX_COMPILER=' + compiler]
        build_dir = a.build_dir / 'windows-x64'
    else:
        if platform.system() != 'Darwin' or platform.machine() != 'arm64':
            p.error('macos-arm64 requires a macOS arm64 host')
        name = 'libspacewars_recast.dylib'
        folder = 'macOS'
        build_dir = a.build_dir
    subprocess.run(['cmake', '-S', str(ROOT / 'native/recast'), '-B', str(build_dir),
                    '-DCMAKE_BUILD_TYPE=Release', *options], check=True)
    subprocess.run(['cmake', '--build', str(build_dir), '--config', 'Release', '-j', '4'], check=True)
    artifact = build_dir / name
    if not artifact.exists():  # Multi-configuration generators, including Visual Studio.
        artifact = build_dir / 'Release' / name
    if a.target == 'windows-x64':
        validate_windows_recast(artifact)
    if a.install:
        target = ROOT / 'unity/Assets/Spacewars/Plugins' / folder / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(artifact, target)
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
