import unittest
from tools.qa.split_scale_report import measured_video_identity


class MeasuredVideoIdentityTests(unittest.TestCase):
    def test_startup_native_size_cannot_label_a_later_1080p_measurement(self):
        rows = [dict(elapsed=1, width=3456, height=2160, vsync=0, target_fps=240),
                dict(elapsed=30, width=1920, height=1080, vsync=0, target_fps=-1)]
        actual = measured_video_identity(rows, 10, 120)
        self.assertEqual([(1920, 1080)], actual['dimensions'])
        self.assertEqual([(-1,)], actual['target_fps'])
        self.assertEqual(1, actual['rows'])

    def test_a_size_change_within_measurement_is_not_hidden(self):
        rows = [dict(elapsed=20, width=1920, height=1080, vsync=0, target_fps=-1),
                dict(elapsed=30, width=3456, height=2160, vsync=0, target_fps=-1)]
        self.assertEqual([(1920, 1080), (3456, 2160)],
                         measured_video_identity(rows, 10, 120)['dimensions'])
