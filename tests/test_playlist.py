import unittest
import os
import sys

# Ensure repository root is on sys.path so m3u8_cf_bypass can be imported
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import parse_master_playlist, derive_referer, format_variant_info, direct_engine_gap


class TestDirectEngineGap(unittest.TestCase):
    """The direct downloader concatenates every URI line. It cannot honour these tags,
    and ignoring them yields a silent or unplayable file, so it must refuse instead."""

    PLAIN_MEDIA = "#EXTM3U\n#EXTINF:4,\nseg0.ts\n#EXTINF:4,\nseg1.ts\n#EXT-X-ENDLIST\n"

    def test_plain_ts_playlist_is_supported(self):
        self.assertIsNone(direct_engine_gap("", self.PLAIN_MEDIA))

    def test_master_without_alternate_renditions_is_supported(self):
        master = '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000,RESOLUTION=640x360\nlow.m3u8\n'
        self.assertIsNone(direct_engine_gap(master, self.PLAIN_MEDIA))

    def test_separate_audio_rendition_is_not_supported(self):
        master = (
            '#EXTM3U\n'
            '#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="en",URI="audio_en.m3u8"\n'
            '#EXT-X-STREAM-INF:BANDWIDTH=1000,AUDIO="aud"\nvideo.m3u8\n'
        )
        self.assertIn("EXT-X-MEDIA", direct_engine_gap(master, self.PLAIN_MEDIA))

    def test_muxed_rendition_without_uri_is_supported(self):
        # An EXT-X-MEDIA entry with no URI describes a track already inside the variant.
        master = '#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="en",DEFAULT=YES\n'
        self.assertIsNone(direct_engine_gap(master, self.PLAIN_MEDIA))

    def test_fmp4_init_segment_is_not_supported(self):
        media = '#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n#EXTINF:4,\nseg0.m4s\n'
        self.assertIn("EXT-X-MAP", direct_engine_gap("", media))

    def test_byte_range_segments_are_not_supported(self):
        media = '#EXTM3U\n#EXTINF:4,\n#EXT-X-BYTERANGE:1000@0\nall.ts\n'
        self.assertIn("EXT-X-BYTERANGE", direct_engine_gap("", media))


class TestPlaylist(unittest.TestCase):
    def test_derive_referer_http_and_https(self):
        self.assertEqual(derive_referer("https://example.com/live/stream.m3u8"), "https://example.com/")
        self.assertEqual(derive_referer("http://sub.domain.org/path/master.m3u8?token=1"), "http://sub.domain.org/")

    def test_derive_referer_host_with_port(self):
        self.assertEqual(derive_referer("http://127.0.0.1:8080/media/index.m3u8"), "http://127.0.0.1:8080/")

    def test_derive_referer_malformed_and_empty(self):
        self.assertEqual(derive_referer("not_a_url/path/file.m3u8"), "not_a_url/path/")
        self.assertEqual(derive_referer(""), "")

    def test_parse_master_playlist_normal_variants_descending(self):
        text = """#EXTM3U
#EXT-X-VERSION:3
#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,FRAME-RATE=29.970,CODECS="avc1.4d401f,mp4a.40.2"
360p.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720,FRAME-RATE=60,CODECS="avc1.640028,mp4a.40.2"
720p.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080,FRAME-RATE=60.000,CODECS="avc1.64002a,mp4a.40.2"
https://cdn.example.com/1080p.m3u8
"""
        master_url = "https://example.com/stream/master.m3u8"
        variants = parse_master_playlist(master_url, text)

        self.assertEqual(len(variants), 3)
        # Should sort descending by bandwidth
        self.assertEqual(variants[0]["bandwidth"], 5000000)
        self.assertEqual(variants[0]["resolution"], "1920x1080")
        self.assertEqual(variants[0]["url"], "https://cdn.example.com/1080p.m3u8")
        self.assertEqual(variants[0]["bandwidth_str"], "5.00 Mbps")
        self.assertEqual(variants[0]["framerate"], "60 fps")

        self.assertEqual(variants[1]["bandwidth"], 2500000)
        self.assertEqual(variants[1]["url"], "https://example.com/stream/720p.m3u8")
        self.assertEqual(variants[1]["resolution"], "1280x720")

        self.assertEqual(variants[2]["bandwidth"], 800000)
        self.assertEqual(variants[2]["bandwidth_str"], "800 Kbps")
        self.assertEqual(variants[2]["framerate"], "29.97 fps")

    def test_parse_master_playlist_no_variants(self):
        text = """#EXTM3U
#EXT-X-VERSION:3
#EXTINF:10.0,
seg1.ts
"""
        variants = parse_master_playlist("https://example.com/master.m3u8", text)
        self.assertEqual(variants, [])

    def test_parse_master_playlist_missing_bandwidth_or_resolution(self):
        text = """#EXTM3U
#EXT-X-STREAM-INF:CODECS="mp4a.40.2"
audio.m3u8
"""
        variants = parse_master_playlist("https://example.com/master.m3u8", text)
        self.assertEqual(len(variants), 1)
        self.assertEqual(variants[0]["bandwidth"], 0)
        self.assertEqual(variants[0]["bandwidth_str"], "Unknown Kbps")
        self.assertEqual(variants[0]["resolution"], "")

    def test_format_variant_info(self):
        full = {
            "resolution": "1920x1080",
            "bandwidth_str": "5.00 Mbps",
            "framerate": "60 fps",
            "codecs": "avc1.64002a,mp4a.40.2"
        }
        self.assertEqual(format_variant_info(full), "Vid | 1920x1080 | 5.00 Mbps | 60 fps | avc1.64002a,mp4a.40.2")

        sparse = {
            "resolution": "",
            "bandwidth_str": "500 Kbps",
            "framerate": "",
            "codecs": ""
        }
        self.assertEqual(format_variant_info(sparse), "Vid | 500 Kbps")


if __name__ == "__main__":
    unittest.main()
