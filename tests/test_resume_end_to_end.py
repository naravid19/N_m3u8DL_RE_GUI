"""
End-to-end regression test for CF-bypass segment resume.

Runs `m3u8_cf_bypass.py` as a real subprocess (matching how the GUI launches
it) against a small in-process HLS origin that always fails two segments, so
the run ends partial (exit 3). The bug this guards: a partial run used to
`shutil.rmtree` its own --seg-dir whenever --keep-segs was not passed (the
GUI's default), deleting the very segments a second attempt needed to resume
from — so every partial download silently reset to 0% on retry. A second run
against a fixed origin, reusing the same --seg-dir, must then finish the
download using only the segments the origin serves on that second pass —
proof the first run's successes actually survived on disk.

Skipped automatically if curl_cffi is not installed, matching the script's
own dependency story.
"""
import http.server
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SCRIPT = os.path.join(ROOT, "m3u8_cf_bypass.py")

try:
    import curl_cffi  # noqa: F401
    HAS_CURL_CFFI = True
except ImportError:
    HAS_CURL_CFFI = False

HAS_FFMPEG = shutil.which("ffmpeg") is not None


def _free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


SEGMENT_COUNT = 5
# Segments at these indices 500 on the first origin instance, succeed on the second.
ALWAYS_FAILS_FIRST_PASS = {1, 3}


def _playlist_body():
    lines = ["#EXTM3U", "#EXT-X-VERSION:3", "#EXT-X-TARGETDURATION:2"]
    for i in range(SEGMENT_COUNT):
        lines.append("#EXTINF:2.0,")
        lines.append(f"seg{i}.ts")
    lines.append("#EXT-X-ENDLIST")
    return ("\n".join(lines) + "\n").encode("utf-8")


def _make_real_ts_segments(count, out_dir):
    """
    Five independently-encoded, genuinely valid MPEG-TS clips (matching how
    real HLS segments are produced) — not placeholder bytes. ffmpeg's concat
    demuxer with -c copy needs decodable streams to write into the output
    .mp4's track boxes; arbitrary bytes make the merge fail for reasons
    unrelated to what this test checks (resume/cleanup), not for the
    behaviour it's meant to catch.
    """
    bodies = []
    for i in range(count):
        path = os.path.join(out_dir, f"fixture_{i}.ts")
        subprocess.run(
            [
                "ffmpeg", "-y", "-loglevel", "error",
                "-f", "lavfi", "-i", f"testsrc=duration=0.5:size=64x64:rate=5",
                "-f", "lavfi", "-i", "sine=duration=0.5",
                "-c:v", "mpeg2video", "-c:a", "mp2", "-f", "mpegts", path,
            ],
            check=True,
            capture_output=True,
        )
        with open(path, "rb") as f:
            bodies.append(f.read())
    return bodies


def _make_origin_handler(fail_indices, segment_bodies):
    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/hls/master.m3u8":
                body = _playlist_body()
                self.send_response(200)
                self.send_header("Content-Type", "application/vnd.apple.mpegurl")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return

            for i in range(SEGMENT_COUNT):
                if self.path == f"/hls/seg{i}.ts":
                    if i in fail_indices:
                        self.send_response(500)
                        self.end_headers()
                        return
                    body = segment_bodies[i]
                    self.send_response(200)
                    self.send_header("Content-Type", "video/mp2t")
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    self.wfile.write(body)
                    return

            self.send_response(404)
            self.end_headers()

        def log_message(self, *args):
            pass

    return Handler


def _run_origin(fail_indices, segment_bodies):
    port = _free_port()
    server = http.server.HTTPServer(("127.0.0.1", port), _make_origin_handler(fail_indices, segment_bodies))
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server, f"http://127.0.0.1:{port}/hls/master.m3u8"


@unittest.skipUnless(HAS_CURL_CFFI, "curl_cffi not installed")
@unittest.skipUnless(HAS_FFMPEG, "ffmpeg not on PATH")
class TestResumeEndToEnd(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture_dir = tempfile.mkdtemp(prefix="nre_resume_fixtures_")
        cls.segment_bodies = _make_real_ts_segments(SEGMENT_COUNT, cls.fixture_dir)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.fixture_dir, ignore_errors=True)

    def setUp(self):
        self.work_dir = tempfile.mkdtemp(prefix="nre_resume_work_")
        self.seg_dir = tempfile.mkdtemp(prefix="nre_resume_segs_")

    def tearDown(self):
        shutil.rmtree(self.work_dir, ignore_errors=True)
        shutil.rmtree(self.seg_dir, ignore_errors=True)

    def _run_script(self, url):
        # Matches what the GUI sets before launching (MainWindow.xaml.cs,
        # StartCloudflareDownloadAsync): without these, a non-ASCII byte
        # anywhere in a caught exception's message crashes the *next*
        # print() on Windows' legacy console codepage, which then reports as
        # a spurious segment failure unrelated to the real one being tested.
        env = dict(os.environ, PYTHONUTF8="1", PYTHONIOENCODING="utf-8")
        return subprocess.run(
            [
                sys.executable, SCRIPT, url,
                "-o", "resume_test.mp4",
                "--work-dir", self.work_dir,
                "--seg-dir", self.seg_dir,
            ],
            capture_output=True,
            text=True,
            timeout=30,
            env=env,
        )

    def test_partial_run_keeps_segments_and_second_run_completes(self):
        failing_origin, failing_url = _run_origin(ALWAYS_FAILS_FIRST_PASS, self.segment_bodies)
        try:
            first = self._run_script(failing_url)
        finally:
            failing_origin.shutdown()
            failing_origin.server_close()

        self.assertEqual(first.returncode, 3, msg=f"stdout:\n{first.stdout}\nstderr:\n{first.stderr}")
        self.assertTrue(
            os.path.isdir(self.seg_dir),
            "seg-dir was deleted after a partial run — resume state was destroyed",
        )
        surviving = sorted(f for f in os.listdir(self.seg_dir) if f.endswith(".ts"))
        self.assertEqual(
            len(surviving), SEGMENT_COUNT - len(ALWAYS_FAILS_FIRST_PASS),
            f"expected the segments that succeeded on the first pass to survive on disk, got {surviving}",
        )

        healthy_origin, healthy_url = _run_origin(set(), self.segment_bodies)
        try:
            second = self._run_script(healthy_url)
        finally:
            healthy_origin.shutdown()
            healthy_origin.server_close()

        self.assertEqual(second.returncode, 0, msg=f"stdout:\n{second.stdout}\nstderr:\n{second.stderr}")
        self.assertIn("Resuming download", second.stdout)
        output_path = os.path.join(self.work_dir, "resume_test.mp4")
        self.assertTrue(os.path.exists(output_path), "merged output missing after the completing resume run")
        self.assertFalse(
            os.path.isdir(self.seg_dir),
            "seg-dir should be cleaned up after a fully successful merge (no --keep-segs)",
        )


if __name__ == "__main__":
    unittest.main()
