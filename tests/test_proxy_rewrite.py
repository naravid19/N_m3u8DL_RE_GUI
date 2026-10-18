import unittest
import os
import sys
from urllib.parse import urljoin

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import (
    make_proxy_url,
    decode_proxy_request,
    looks_like_manifest_url,
    sniff_manifest_kind,
    rewrite_hls_manifest,
    rewrite_dash_manifest,
    rewrite_mss_manifest,
)

PROXY_BASE = "http://127.0.0.1:54321"
TOKEN = "testtoken123"


class TestProxyUrlCodec(unittest.TestCase):
    """make_proxy_url / decode_proxy_request must round-trip, and the encoding
    must survive urljoin() the way a DASH SegmentTemplate needs it to (N2)."""

    def test_round_trip_simple_path(self):
        target = "https://cdn.example.com/hls/seg1.ts"
        proxied = make_proxy_url(PROXY_BASE, target, TOKEN)
        decoded, err = decode_proxy_request(proxied[len(PROXY_BASE):], TOKEN)
        self.assertIsNone(err)
        self.assertEqual(decoded, target)

    def test_round_trip_with_query_string(self):
        target = "https://cdn.example.com/hls/seg1.ts?token=abc&exp=123"
        proxied = make_proxy_url(PROXY_BASE, target, TOKEN)
        decoded, err = decode_proxy_request(proxied[len(PROXY_BASE):], TOKEN)
        self.assertIsNone(err)
        self.assertEqual(decoded, target)

    def test_round_trip_with_port(self):
        target = "http://127.0.0.1:8080/media/index.m3u8"
        proxied = make_proxy_url(PROXY_BASE, target, TOKEN)
        decoded, err = decode_proxy_request(proxied[len(PROXY_BASE):], TOKEN)
        self.assertIsNone(err)
        self.assertEqual(decoded, target)

    def test_round_trip_deep_path(self):
        target = "https://cdn.example.com/v/1/2024/08/23/chunk_5.m4s"
        proxied = make_proxy_url(PROXY_BASE, target, TOKEN)
        decoded, err = decode_proxy_request(proxied[len(PROXY_BASE):], TOKEN)
        self.assertIsNone(err)
        self.assertEqual(decoded, target)

    def test_wrong_token_is_rejected(self):
        target = "https://cdn.example.com/hls/seg1.ts"
        proxied = make_proxy_url(PROXY_BASE, target, TOKEN)
        decoded, err = decode_proxy_request(proxied[len(PROXY_BASE):], "different-token")
        self.assertIsNone(decoded)
        self.assertIsNotNone(err)

    def test_a_non_ascii_token_is_rejected_rather_than_raising(self):
        # The token is compared in constant time, and hmac.compare_digest raises
        # TypeError for a non-ASCII str -- which a request path can carry.
        decoded, err = decode_proxy_request("/u/t" + chr(0xF6) + "ken/https/cdn.example.com/a.ts", TOKEN)
        self.assertIsNone(decoded)
        self.assertIsNotNone(err)

    def test_malformed_route_is_rejected(self):
        decoded, err = decode_proxy_request("/not/a/proxy/route", TOKEN)
        self.assertIsNone(decoded)
        self.assertIsNotNone(err)

    def test_non_http_scheme_is_rejected(self):
        decoded, err = decode_proxy_request(f"/u/{TOKEN}/ftp/cdn.example.com/file.ts", TOKEN)
        self.assertIsNone(decoded)
        self.assertIsNotNone(err)

    def test_base_url_join_preserves_dash_template_placeholders(self):
        """The bug this whole scheme exists to fix: urljoin(proxied_base_dir,
        'chunk_$Number$.m4s') must keep $Number$ literal, not have it eaten
        by a query string the way the old /proxy?url=... scheme did."""
        base_dir = "https://cdn.example.com/v/1/"
        proxied_base = make_proxy_url(PROXY_BASE, base_dir, TOKEN)
        resolved = urljoin(proxied_base, "chunk_$Number$.m4s")

        self.assertTrue(resolved.startswith(proxied_base))
        self.assertIn("chunk_$Number$.m4s", resolved)

        decoded, err = decode_proxy_request(resolved[len(PROXY_BASE):], TOKEN)
        self.assertIsNone(err)
        self.assertEqual(decoded, "https://cdn.example.com/v/1/chunk_$Number$.m4s")


class TestManifestDetection(unittest.TestCase):
    def test_extensions_recognised(self):
        for ext in (".m3u8", ".m3u", ".mpd", ".ism", ".isml"):
            self.assertTrue(looks_like_manifest_url(f"https://cdn.example.com/x{ext}"))

    def test_extensionless_manifest_path_recognised(self):
        self.assertTrue(looks_like_manifest_url("https://cdn.example.com/stream/Manifest"))

    def test_segment_extension_not_recognised(self):
        self.assertFalse(looks_like_manifest_url("https://cdn.example.com/seg1.ts"))

    def test_sniff_hls(self):
        self.assertEqual(sniff_manifest_kind("#EXTM3U\n#EXTINF:10,\nseg1.ts\n"), "hls")

    def test_sniff_dash(self):
        self.assertEqual(sniff_manifest_kind('<?xml version="1.0"?><MPD xmlns="x"></MPD>'), "dash")

    def test_sniff_mss(self):
        self.assertEqual(sniff_manifest_kind('<SmoothStreamingMedia Duration="1"></SmoothStreamingMedia>'), "mss")

    def test_sniff_unknown_returns_none(self):
        self.assertIsNone(sniff_manifest_kind("not a manifest at all"))


class TestRewriteHls(unittest.TestCase):
    def setUp(self):
        self.base_url = "https://cdn.example.com/hls/master.m3u8"

    def test_segments_and_key_uri_are_proxied(self):
        text = (
            "#EXTM3U\n"
            "#EXT-X-VERSION:3\n"
            '#EXT-X-KEY:METHOD=AES-128,URI="https://key.example.com/key.bin",IV=0x01\n'
            "#EXTINF:10.0,\n"
            "seg1.ts\n"
            "#EXTINF:10.0,\n"
            "https://cdn.example.com/seg2.ts\n"
        )
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)

        key_line = [l for l in out.splitlines() if l.startswith("#EXT-X-KEY")][0]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://key.example.com/key.bin", TOKEN), key_line)

        seg_lines = [l for l in out.splitlines() if l.startswith("http")]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/hls/seg1.ts", TOKEN), seg_lines)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/seg2.ts", TOKEN), seg_lines)

    def test_ext_x_map_uri_is_proxied(self):
        text = '#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n#EXTINF:10.0,\nseg1.ts\n'
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        map_line = [l for l in out.splitlines() if l.startswith("#EXT-X-MAP")][0]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/hls/init.mp4", TOKEN), map_line)

    def test_ext_x_media_uri_is_proxied(self):
        text = (
            "#EXTM3U\n"
            '#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="Thai",DEFAULT=YES,URI="audio_th.m3u8"\n'
            "#EXTINF:10.0,\n"
            "seg1.ts\n"
        )
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        media_line = [l for l in out.splitlines() if l.startswith("#EXT-X-MEDIA")][0]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/hls/audio_th.m3u8", TOKEN), media_line)

    def test_ext_x_preload_hint_uri_is_proxied(self):
        text = '#EXTM3U\n#EXT-X-PRELOAD-HINT:TYPE=PART,URI="part1.mp4"\n'
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        hint_line = [l for l in out.splitlines() if l.startswith("#EXT-X-PRELOAD-HINT")][0]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/hls/part1.mp4", TOKEN), hint_line)

    def test_ext_x_i_frame_stream_inf_uri_is_proxied(self):
        text = '#EXTM3U\n#EXT-X-I-FRAME-STREAM-INF:BANDWIDTH=86000,URI="iframe.m3u8"\n'
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        iframe_line = [l for l in out.splitlines() if l.startswith("#EXT-X-I-FRAME-STREAM-INF")][0]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/hls/iframe.m3u8", TOKEN), iframe_line)

    def test_ext_x_rendition_report_uri_is_proxied(self):
        text = '#EXTM3U\n#EXT-X-RENDITION-REPORT:URI="../1M/waitProgram.m3u8",LAST-MSN=273,LAST-PART=2\n'
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        report_line = [l for l in out.splitlines() if l.startswith("#EXT-X-RENDITION-REPORT")][0]
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/1M/waitProgram.m3u8", TOKEN), report_line)

    def test_master_variant_line_is_proxied_not_the_stream_inf_tag(self):
        text = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000\nmedia.m3u8\n"
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        self.assertIn("#EXT-X-STREAM-INF:BANDWIDTH=1000", out)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/hls/media.m3u8", TOKEN), out)

    def test_blank_lines_and_unknown_tags_pass_through(self):
        text = "#EXTM3U\n\n#EXT-X-TARGETDURATION:6\n#EXTINF:10.0,\nseg1.ts\n"
        out = rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN)
        self.assertIn("#EXT-X-TARGETDURATION:6", out)
        self.assertIn("", out.splitlines())

    def test_allowed_hosts_collects_every_referenced_host(self):
        text = (
            "#EXTM3U\n"
            '#EXT-X-KEY:METHOD=AES-128,URI="https://keys.other-cdn.com/k.bin"\n'
            "#EXTINF:10.0,\n"
            "https://segs.other-cdn.com/seg1.ts\n"
        )
        allowed = {"cdn.example.com"}
        rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN, allowed_hosts=allowed)
        self.assertIn("keys.other-cdn.com", allowed)
        self.assertIn("segs.other-cdn.com", allowed)

    def test_allowed_hosts_never_admits_an_internal_address(self):
        # A manifest can name any address. Admitting these would let a hostile
        # upstream use the proxy as a relay onto this machine or the cloud
        # metadata service.
        text = (
            "#EXTM3U\n"
            '#EXT-X-KEY:METHOD=AES-128,URI="http://169.254.169.254/latest/meta-data/"\n'
            "#EXTINF:10.0,\n"
            "http://127.0.0.1:8080/seg1.ts\n"
        )
        allowed = {"cdn.example.com"}
        rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN, allowed_hosts=allowed)
        self.assertEqual(allowed, {"cdn.example.com"})

    def test_allowed_hosts_never_admits_other_spellings_of_loopback(self):
        # curl connects to loopback for every one of these: it also percent-decodes
        # and IDN-maps a host (full-width digits) before connecting.
        hosts = ("localhost:8080", "localhost.", "app.localhost", "127.1", "2130706433", "0x7f.1",
                 "127.0.0.%31", "loc%61lhost", "１２７.0.0.1", "%EF%BC%91%EF%BC%92%EF%BC%97.0.0.1")
        text = "#EXTM3U\n" + "".join(f"#EXTINF:10.0,\nhttp://{h}/seg.ts\n" for h in hosts)
        allowed = {"cdn.example.com"}
        rewrite_hls_manifest(self.base_url, text, PROXY_BASE, TOKEN, allowed_hosts=allowed)
        self.assertEqual(allowed, {"cdn.example.com"})


class TestRewriteDash(unittest.TestCase):
    def setUp(self):
        self.mpd_url = "https://cdn.example.com/dash/manifest.mpd"

    def test_injects_base_url_when_absent(self):
        text = (
            '<?xml version="1.0" encoding="UTF-8"?>\n'
            '<MPD xmlns="urn:mpeg:dash:schema:mpd:2011">\n'
            "  <Period><AdaptationSet mimeType=\"video/mp4\"><Representation id=\"1\" bandwidth=\"2000000\">\n"
            '    <SegmentTemplate media="chunk_$Number$.m4s" initialization="init.mp4"/>\n'
            "  </Representation></AdaptationSet></Period>\n"
            "</MPD>\n"
        )
        out = rewrite_dash_manifest(self.mpd_url, text, PROXY_BASE, TOKEN)
        self.assertIn("<BaseURL>", out)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/dash/", TOKEN), out)
        # The template placeholder must survive completely untouched.
        self.assertIn('media="chunk_$Number$.m4s"', out)

    def test_injects_base_url_preserves_query_string(self):
        url_with_query = "https://cdn.example.com/dash/manifest.mpd?token=secret123&exp=999"
        text = '<MPD xmlns="urn:mpeg:dash:schema:mpd:2011"><Period></Period></MPD>'
        out = rewrite_dash_manifest(url_with_query, text, PROXY_BASE, TOKEN)
        self.assertIn("<BaseURL>", out)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/dash/?token=secret123&exp=999", TOKEN), out)

    def test_replaces_existing_base_url(self):
        text = (
            '<MPD xmlns="x">'
            "<BaseURL>seg/</BaseURL>"
            "<Period></Period></MPD>"
        )
        out = rewrite_dash_manifest(self.mpd_url, text, PROXY_BASE, TOKEN)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/dash/seg/", TOKEN), out)

    def test_replaces_existing_relative_base_url_preserves_query_string(self):
        url_with_query = "https://cdn.example.com/dash/manifest.mpd?auth=tok123"
        text = '<MPD xmlns="x"><BaseURL>tracks/</BaseURL></MPD>'
        out = rewrite_dash_manifest(url_with_query, text, PROXY_BASE, TOKEN)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://cdn.example.com/dash/tracks/?auth=tok123", TOKEN), out)

    def test_absolute_existing_base_url_is_rewritten(self):
        text = '<MPD xmlns="x"><BaseURL>https://other-cdn.example.com/base/</BaseURL></MPD>'
        out = rewrite_dash_manifest(self.mpd_url, text, PROXY_BASE, TOKEN)
        self.assertIn(make_proxy_url(PROXY_BASE, "https://other-cdn.example.com/base/", TOKEN), out)

    def test_injected_base_url_resolves_number_template_via_urljoin(self):
        """End-to-end proof that N2 is fixed: resolving the emitted BaseURL
        against a $Number$ template keeps the placeholder literal and decodes
        back to the real segment URL."""
        text = (
            '<MPD xmlns="x"><Period><AdaptationSet><Representation>'
            '<SegmentTemplate media="chunk_$Number$.m4s" startNumber="1"/>'
            "</Representation></AdaptationSet></Period></MPD>"
        )
        out = rewrite_dash_manifest(self.mpd_url, text, PROXY_BASE, TOKEN)
        base = out.split("<BaseURL>")[1].split("</BaseURL>")[0]
        resolved = urljoin(base, "chunk_5.m4s")
        decoded, err = decode_proxy_request(resolved[len(PROXY_BASE):], TOKEN)
        self.assertIsNone(err)
        self.assertEqual(decoded, "https://cdn.example.com/dash/chunk_5.m4s")

    def test_allowed_hosts_collects_base_url_host(self):
        text = '<MPD xmlns="x"><BaseURL>https://segs.other-cdn.com/</BaseURL></MPD>'
        allowed = {"cdn.example.com"}
        rewrite_dash_manifest(self.mpd_url, text, PROXY_BASE, TOKEN, allowed_hosts=allowed)
        self.assertIn("segs.other-cdn.com", allowed)


class TestRewriteMss(unittest.TestCase):
    def test_is_a_deliberate_no_op(self):
        text = '<SmoothStreamingMedia><StreamIndex Url="QualityLevels({bitrate})/Fragments(video={start time})"/></SmoothStreamingMedia>'
        out = rewrite_mss_manifest("https://cdn.example.com/stream/Manifest", text, PROXY_BASE, TOKEN)
        self.assertEqual(out, text)

    def test_tracks_the_manifest_host(self):
        allowed = set()
        rewrite_mss_manifest("https://cdn.example.com/stream/Manifest", "<x/>", PROXY_BASE, TOKEN, allowed_hosts=allowed)
        self.assertIn("cdn.example.com", allowed)


if __name__ == "__main__":
    unittest.main()
