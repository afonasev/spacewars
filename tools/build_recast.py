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
    p.add_argument('--install', action='store_true', help='Copy the tested host plugin into Unity Plugins/macOS')
    a = p.parse_args()
    if platform.system() != 'Darwin' or platform.machine() != 'arm64':
        p.error('First qualified target is macOS arm64; other targets remain NotRun')
    subprocess.run(['cmake', '-S', str(ROOT / 'native/recast'), '-B', str(a.build_dir), '-DCMAKE_BUILD_TYPE=Release'], check=True)
    subprocess.run(['cmake', '--build', str(a.build_dir), '-j', '4'], check=True)
    if a.install:
        target = ROOT / 'unity/Assets/Spacewars/Plugins/macOS/libspacewars_recast.dylib'
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(a.build_dir / target.name, target)
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
