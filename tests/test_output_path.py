import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import output_path

BACKSLASH = chr(92)


class TestOutputPath(unittest.TestCase):
    """-o names the merged file; it must not be able to put it outside --work-dir."""

    OUT = os.path.abspath("work")

    def test_a_plain_name_gets_the_mp4_extension(self):
        self.assertEqual(output_path(self.OUT, "clip"), os.path.join(self.OUT, "clip.mp4"))

    def test_an_absolute_output_stays_inside_the_work_dir(self):
        elsewhere = os.path.abspath(os.path.join(os.sep, "elsewhere", "clip.mp4"))
        self.assertEqual(output_path(self.OUT, elsewhere), os.path.join(self.OUT, "clip.mp4"))

    def test_parent_references_stay_inside_the_work_dir(self):
        for sep in ("/", BACKSLASH):
            self.assertEqual(output_path(self.OUT, f"..{sep}..{sep}clip.mp4"), os.path.join(self.OUT, "clip.mp4"))


if __name__ == "__main__":
    unittest.main()
