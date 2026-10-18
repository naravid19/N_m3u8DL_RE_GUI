import unittest
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import check_encryption


class TestEncryption(unittest.TestCase):
    def test_unencrypted_playlist_returns_false(self):
        text = """#EXTM3U
#EXT-X-VERSION:3
#EXTINF:10.0,
seg1.ts
"""
        self.assertFalse(check_encryption(text))

    def test_method_none_returns_false(self):
        text = """#EXTM3U
#EXT-X-VERSION:3
#EXT-X-KEY:METHOD=NONE
#EXTINF:10.0,
seg1.ts
"""
        self.assertFalse(check_encryption(text))

    def test_aes_128_returns_true(self):
        text = """#EXTM3U
#EXT-X-VERSION:3
#EXT-X-KEY:METHOD=AES-128,URI="https://key.example.com/key.bin",IV=0x01
#EXTINF:10.0,
seg1.ts
"""
        self.assertTrue(check_encryption(text))

    def test_sample_aes_returns_true(self):
        text = """#EXTM3U
#EXT-X-VERSION:5
#EXT-X-KEY:METHOD=SAMPLE-AES,URI="skd://drm.example.com",KEYFORMAT="com.apple.streamingkeydelivery"
#EXTINF:10.0,
seg1.ts
"""
        self.assertTrue(check_encryption(text))


if __name__ == "__main__":
    unittest.main()
