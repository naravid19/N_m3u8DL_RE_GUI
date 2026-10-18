import unittest
import os
import sys
import tempfile
import shutil

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import download_segments


class FakeSession:
    def __init__(self, failing_indices=None, headers_by_index=None):
        self.failing_indices = failing_indices or set()
        self.headers_by_index = headers_by_index or {}

    def get(self, url, headers=None, timeout=60, **kwargs):
        # Extract index from url e.g. http://example.com/seg_00003.ts
        idx = int(url.split("seg_")[-1].split(".")[0])
        class Response:
            def __init__(self, code, content, headers=None):
                self.status_code = code
                self.content = content
                self.headers = headers or {"Content-Length": str(len(content))}

            def iter_content(self, chunk_size=None):
                # Two chunks, like a real streamed body.
                yield self.content[:3]
                yield self.content[3:]

        if idx in self.failing_indices:
            return Response(500, b"")
        return Response(200, b"TS_DATA", self.headers_by_index.get(idx))


class TestSegments(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_all_segments_succeed(self):
        segs = [f"http://example.com/seg_{i:05d}.ts" for i in range(5)]
        make_session = lambda: FakeSession()

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=2, retry_delay=0)
        self.assertEqual(count, 5)
        self.assertEqual(len(results), 5)

    def test_gap_in_middle_returns_contiguous_prefix(self):
        # Segments 0, 1, 2 pass; 3 fails; 4 passes
        segs = [f"http://example.com/seg_{i:05d}.ts" for i in range(5)]
        make_session = lambda: FakeSession(failing_indices={3})

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=2, retry_delay=0)
        # Should return 3 as contiguous count (0, 1, 2)
        self.assertEqual(count, 3)
        self.assertIn(0, results)
        self.assertIn(1, results)
        self.assertIn(2, results)
        self.assertNotIn(3, results)

    def test_first_segment_fails_returns_zero(self):
        segs = [f"http://example.com/seg_{i:05d}.ts" for i in range(3)]
        make_session = lambda: FakeSession(failing_indices={0})

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=2, retry_delay=0)
        self.assertEqual(count, 0)

    def test_segment_shorter_than_its_content_length_is_not_accepted(self):
        # The server promised 100 bytes; the body stopped after 7.
        segs = [f"http://example.com/seg_{i:05d}.ts" for i in range(2)]
        make_session = lambda: FakeSession(headers_by_index={1: {"Content-Length": "100"}})

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=2, retry_delay=0)

        self.assertEqual(count, 1)
        self.assertNotIn(1, results)
        # A leftover file would be taken as finished by the next run's resume scan.
        self.assertFalse(os.path.exists(os.path.join(self.temp_dir, "00001.ts")))

    def test_compressed_segment_is_accepted_though_its_content_length_differs(self):
        # curl hands over the decoded body, but Content-Length counts the compressed bytes.
        segs = ["http://example.com/seg_00000.ts"]
        make_session = lambda: FakeSession(headers_by_index={0: {"Content-Length": "3", "Content-Encoding": "gzip"}})

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=1, retry_delay=0)

        self.assertEqual(count, 1)

    def test_segment_with_a_repeated_content_length_is_accepted(self):
        # curl accepts a header sent twice with the same value; curl_cffi reports it as "7, 7".
        segs = ["http://example.com/seg_00000.ts"]
        make_session = lambda: FakeSession(headers_by_index={0: {"Content-Length": "7, 7"}})

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=1, retry_delay=0)

        self.assertEqual(count, 1)

    def test_resumes_pre_existing_segments(self):
        # Pre-create segments 0 and 1
        seg0 = os.path.join(self.temp_dir, "00000.ts")
        seg1 = os.path.join(self.temp_dir, "00001.ts")
        with open(seg0, "wb") as f:
            f.write(b"PRE_EXISTING_SEG_0")
        with open(seg1, "wb") as f:
            f.write(b"PRE_EXISTING_SEG_1")

        # FakeSession will fail if called on 0 or 1
        segs = [f"http://example.com/seg_{i:05d}.ts" for i in range(4)]
        make_session = lambda: FakeSession(failing_indices={0, 1})

        count, results = download_segments(make_session, segs, self.temp_dir, {}, workers=2, retry_delay=0)
        self.assertEqual(count, 4)
        self.assertEqual(len(results), 4)

        # Verify pre-existing content was preserved and not corrupted
        with open(seg0, "rb") as f:
            self.assertEqual(f.read(), b"PRE_EXISTING_SEG_0")
        with open(seg1, "rb") as f:
            self.assertEqual(f.read(), b"PRE_EXISTING_SEG_1")


if __name__ == "__main__":
    unittest.main()
