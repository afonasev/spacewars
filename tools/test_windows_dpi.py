"""Verify the real Windows helper packaging route and PE resource rejection cases."""
import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

SOURCE = Path(__file__).parent / 'native-release/package.py'
spec = importlib.util.spec_from_file_location('dpi_package', SOURCE)
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class WindowsDpiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.compiler = shutil.which('x86_64-w64-mingw32-g++')
        cls.windres = shutil.which('x86_64-w64-mingw32-windres')
        if not cls.compiler or not cls.windres or not shutil.which('go'):
            raise RuntimeError('Windows resource QA requires Go, mingw compiler and windres')
        cls.temp = tempfile.TemporaryDirectory(prefix='spacewars-dpi-test-')
        cls.addClassCleanup(cls.temp.cleanup)
        cls.directory = Path(cls.temp.name) / 'resources with spaces'
        cls.directory.mkdir()
        cls.source = cls.directory / 'probe.cpp'
        cls.source.write_text('int main(){return 0;}\n')

    def compile_probe(self, name, manifest=None):
        rc = self.directory / (name + '.rc')
        resource = self.directory / (name + '.o')
        icon = package.ROOT / 'tools/native-release/icons/Spacewars.ico'
        script = f'1 ICON "{icon.as_posix()}"\n'
        if manifest:
            script += f'1 24 "{manifest.as_posix()}"\n'
        rc.write_text(script)
        subprocess.run([self.windres, str(rc), str(resource)], check=True, capture_output=True)
        exe = self.directory / (name + '.exe')
        subprocess.run([self.compiler, str(self.source), str(resource), '-o', str(exe)],
                       check=True, capture_output=True)
        return exe

    def test_real_helpers_and_bridge_embed_dpi_and_icon(self):
        destination = self.directory / 'Spacewars.exe'
        env = dict(os.environ, CGO_ENABLED='0', GOOS='windows', GOARCH='amd64')
        package.build_windows_helper(destination, '-s -w -H windowsgui -X main.channel=test',
                                     env, bridge=True)
        expected = ET.tostring(ET.fromstring((SOURCE.parent / 'bootstrap.manifest').read_bytes()))
        for exe in (destination, Path(str(destination) + '-bridge')):
            package.validate_windows_dpi(exe)
            self.assertEqual(ET.tostring(ET.fromstring(package.windows_manifest(exe))), expected)
            # Extract the group-icon resource independently with the native tool.
            dump = subprocess.check_output(['x86_64-w64-mingw32-windres', '-J', 'coff', '-O', 'rc', str(exe)],
                                           stderr=subprocess.STDOUT, text=True)
            self.assertIn('GROUP_ICON', dump)
        self.assertFalse((package.UPDATER / 'rsrc.syso').exists())

    def test_previous_icon_only_helper_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'manifest is missing'):
            package.validate_windows_dpi(self.compile_probe('previous'))

    def test_appended_xml_is_not_a_process_manifest(self):
        exe = self.compile_probe('appended')
        with exe.open('ab') as stream:
            stream.write((SOURCE.parent / 'bootstrap.manifest').read_bytes())
        with self.assertRaisesRegex(ValueError, 'manifest is missing'):
            package.validate_windows_dpi(exe)

    def test_unaware_embedded_manifest_is_rejected(self):
        manifest = self.directory / 'unaware.manifest'
        manifest.write_text((SOURCE.parent / 'bootstrap.manifest').read_text()
                            .replace('PerMonitorV2, PerMonitor', 'unaware'))
        with self.assertRaisesRegex(ValueError, 'per-monitor DPI awareness'):
            package.validate_windows_dpi(self.compile_probe('unaware', manifest))

    def test_truncated_executable_is_rejected(self):
        exe = self.directory / 'truncated.exe'
        exe.write_bytes(b'MZ')
        with self.assertRaisesRegex(ValueError, 'Truncated Windows PE'):
            package.validate_windows_dpi(exe)


if __name__ == '__main__':
    unittest.main()
