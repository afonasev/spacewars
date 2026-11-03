import unittest
from release_notes import with_installer_downloads

class DownloadPresentation(unittest.TestCase):
 def test_links_preserve_canonical_notes_and_repeat_safely(self):
  notes='## Русский\n\n### Исправления\n- Исправлено.\n\n## English\n\n### Fixed\n- Fixed.\n'
  args=('9.8.7','afonasev/game','Game-9.8.7.exe','Game-9.8.7.pkg')
  body=with_installer_downloads(notes,*args)
  self.assertTrue(body.startswith('## Русский\n'))
  self.assertEqual(with_installer_downloads(body,*args),body)
  self.assertIn('/afonasev/game/releases/download/v9.8.7/Game-9.8.7.exe',body)
  self.assertIn('tag%3Av9%20prerelease%3Afalse',body)
  self.assertEqual(body.count('- Исправлено.'),1)
  self.assertEqual(body.count('- Fixed.'),1)
 def test_rejects_non_native_versions_and_non_installer_names(self):
  with self.assertRaises(ValueError):with_installer_downloads('', 'content-42','afonasev/game','a.exe','a.dmg')
  with self.assertRaises(ValueError):with_installer_downloads('', '1.2.3','afonasev/game','../a.exe','a.dmg')
if __name__=='__main__':unittest.main()
