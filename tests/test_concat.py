import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import concat_segments_binary


class TestBinaryConcat(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_concatenates_segments_in_input_order(self):
        parts = [b"first", b"second", b"third"]
        paths = []
        for index, body in enumerate(parts):
            path = os.path.join(self.temp_dir, f"{index}.ts")
            with open(path, "wb") as output:
                output.write(body)
            paths.append(path)

        output_path = os.path.join(self.temp_dir, "combined.ts")
        written = concat_segments_binary(paths, output_path)

        self.assertEqual(written, len(b"".join(parts)))
        with open(output_path, "rb") as combined:
            self.assertEqual(combined.read(), b"firstsecondthird")


if __name__ == "__main__":
    unittest.main()
