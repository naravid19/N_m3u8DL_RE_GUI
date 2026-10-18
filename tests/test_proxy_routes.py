"""
Integration tests for `m3u8_cf_bypass.py --serve`.

These run the script as a real subprocess (matching how the GUI launches it)
against a small in-process origin server, and exercise the actual
ThreadingHTTPServer route handling that the pure-function tests in
test_proxy_rewrite.py cannot reach: token enforcement, the SSRF host
allowlist and redirect handling, Range forwarding, and the stdin-close
shutdown handshake (N1/N4).

Skipped automatically if curl_cffi is not installed, matching the script's
own dependency story rather than adding a test-only dependency.
"""
import gzip
import http.client
import http.server
import os
import re
import socket
import subprocess
import sys
import threading
import time
import unittest
import urllib.error
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SCRIPT = os.path.join(ROOT, "m3u8_cf_bypass.py")

try:
    import curl_cffi  # noqa: F401
    HAS_CURL_CFFI = True
except ImportError:
    HAS_CURL_CFFI = False


def _free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    return port


MPD_BODY = (
    '<?xml version="1.0"?>\n'
    '<MPD xmlns="urn:mpeg:dash:schema:mpd:2011">\n'
    '  <Period><AdaptationSet mimeType="video/mp4"><Representation id="1" bandwidth="1000000">\n'
    '    <SegmentTemplate media="chunk_$Number$.m4s" initialization="init.mp4" startNumber="1"/>\n'
    "  </Representation></AdaptationSet></Period>\n"
    "</MPD>\n"
)
SEGMENT_BODY = b"0123456789" * 10  # 100 bytes, enough to exercise a Range request


class _OriginHandler(http.server.BaseHTTPRequestHandler):
    # path -> Location; setUpClass adds "/redirect-internal" once the internal server's port is known.
    redirects = {"/moved.mpd": "/dash/manifest.mpd", "/loop-a": "/loop-b", "/loop-b": "/loop-a"}

    def do_GET(self):
        if self.path == "/dash/manifest.mpd":
            body = MPD_BODY.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/dash+xml")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        elif self.path == "/dash/chunk_1.m4s":
            range_header = self.headers.get("Range")
            if range_header:
                # Serve bytes=10-19 style ranges for the Range-forwarding test.
                m = re.match(r"bytes=(\d+)-(\d+)", range_header)
                start, end = int(m.group(1)), int(m.group(2))
                chunk = SEGMENT_BODY[start:end + 1]
                self.send_response(206)
                self.send_header("Content-Type", "video/mp4")
                self.send_header("Content-Length", str(len(chunk)))
                self.send_header("Content-Range", f"bytes {start}-{end}/{len(SEGMENT_BODY)}")
                self.end_headers()
                self.wfile.write(chunk)
            else:
                self.send_response(200)
                self.send_header("Content-Type", "video/mp4")
                self.send_header("Content-Length", str(len(SEGMENT_BODY)))
                self.end_headers()
                self.wfile.write(SEGMENT_BODY)
        elif self.path == "/dash/chunked.m4s":
            # No Content-Length: the body is delimited by chunk framing only.
            self.protocol_version = "HTTP/1.1"
            self.close_connection = True
            self.send_response(200)
            self.send_header("Content-Type", "video/mp4")
            self.send_header("Transfer-Encoding", "chunked")
            self.send_header("Connection", "close")
            self.end_headers()
            half = len(SEGMENT_BODY) // 2
            for part in (SEGMENT_BODY[:half], SEGMENT_BODY[half:]):
                self.wfile.write(b"%x\r\n" % len(part) + part + b"\r\n")
            self.wfile.write(b"0\r\n\r\n")
        elif self.path == "/dash/gzip.m4s":
            # Content-Length is the *encoded* size, which is not the size of the
            # bytes a decoding client (curl_cffi) hands on.
            encoded = gzip.compress(SEGMENT_BODY * 20)
            self.send_response(200)
            self.send_header("Content-Type", "video/mp4")
            self.send_header("Content-Encoding", "gzip")
            self.send_header("Content-Length", str(len(encoded)))
            self.end_headers()
            self.wfile.write(encoded)
        elif self.path in self.redirects:
            self.send_response(302)
            self.send_header("Location", self.redirects[self.path])
            self.send_header("Content-Length", "0")
            self.end_headers()
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, *args):
        pass


class _InternalHandler(http.server.BaseHTTPRequestHandler):
    """Stands in for a service only reachable from this machine."""
    hits = 0

    def do_GET(self):
        type(self).hits += 1
        body = b"SECRET"
        self.send_response(200)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *args):
        pass


@unittest.skipUnless(HAS_CURL_CFFI, "curl_cffi not installed")
class TestProxyServerIntegration(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.origin_port = _free_port()
        cls.origin = http.server.HTTPServer(("127.0.0.1", cls.origin_port), _OriginHandler)
        cls.origin_thread = threading.Thread(target=cls.origin.serve_forever, daemon=True)
        cls.origin_thread.start()
        cls.target_url = f"http://127.0.0.1:{cls.origin_port}/dash/manifest.mpd"

        internal_port = _free_port()
        cls.internal = http.server.HTTPServer(("127.0.0.1", internal_port), _InternalHandler)
        threading.Thread(target=cls.internal.serve_forever, daemon=True).start()
        _OriginHandler.redirects["/redirect-internal"] = f"http://127.0.0.1:{internal_port}/secret"

    @classmethod
    def tearDownClass(cls):
        cls.origin.shutdown()
        cls.origin.server_close()
        cls.internal.shutdown()
        cls.internal.server_close()

    def setUp(self):
        self.proxy = subprocess.Popen(
            [sys.executable, SCRIPT, self.target_url, "--serve"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
        )
        self.proxy_url = self._read_ready_line(timeout=10)

    def tearDown(self):
        try:
            if self.proxy.stdin and not self.proxy.stdin.closed:
                self.proxy.stdin.close()
            self.proxy.wait(timeout=5)
        except Exception:
            self.proxy.kill()
        finally:
            for stream in (self.proxy.stdout, self.proxy.stderr):
                if stream and not stream.closed:
                    stream.close()

    def _read_ready_line(self, timeout):
        deadline = time.time() + timeout
        while time.time() < deadline:
            line = self.proxy.stdout.readline()
            if not line:
                break
            if line.startswith("PROXY_READY"):
                return line.strip().split(" ", 1)[1]
        stderr = self.proxy.stderr.read()
        self.fail(f"proxy never printed PROXY_READY. stderr:\n{stderr}")

    def _proxied(self, origin_path):
        return self.proxy_url.replace("/dash/manifest.mpd", origin_path)

    def test_manifest_is_rewritten_and_segment_is_reachable(self):
        mpd = urllib.request.urlopen(self.proxy_url, timeout=5).read().decode("utf-8")
        self.assertIn("<BaseURL>", mpd)
        self.assertIn('chunk_$Number$.m4s', mpd)

        base = re.search(r"<BaseURL>(.*?)</BaseURL>", mpd).group(1)
        from urllib.parse import urljoin
        seg_url = urljoin(base, "chunk_1.m4s")

        seg = urllib.request.urlopen(seg_url, timeout=5)
        self.assertEqual(seg.status, 200)
        self.assertEqual(seg.read(), SEGMENT_BODY)

    def _segment_url(self, name):
        from urllib.parse import urljoin
        mpd = urllib.request.urlopen(self.proxy_url, timeout=5).read().decode("utf-8")
        base = re.search(r"<BaseURL>(.*?)</BaseURL>", mpd).group(1)
        return urljoin(base, name)

    def _get_keep_alive(self, url):
        """GET over a persistent HTTP/1.1 connection, the way RE's HttpClient does.

        urllib sends `Connection: close`, which hides framing bugs: the server closes
        the socket after the reply, so a missing Content-Length still "ends" the body.
        """
        from urllib.parse import urlsplit
        parts = urlsplit(url)
        conn = http.client.HTTPConnection(parts.hostname, parts.port, timeout=5)
        try:
            conn.request("GET", parts.path)
            resp = conn.getresponse()
            return resp.status, resp.read()
        finally:
            conn.close()

    def test_chunked_origin_body_arrives_complete_over_keep_alive(self):
        # curl_cffi de-chunks, so the origin reply has no Content-Length to forward.
        # Without an explicit end-of-body the keep-alive client waits for more bytes.
        status, body = self._get_keep_alive(self._segment_url("chunked.m4s"))
        self.assertEqual(status, 200)
        self.assertEqual(body, SEGMENT_BODY)

    def test_gzip_origin_body_is_not_cut_at_the_encoded_length(self):
        # Content-Length names the compressed size; curl_cffi hands on the decoded bytes.
        status, body = self._get_keep_alive(self._segment_url("gzip.m4s"))
        self.assertEqual(status, 200)
        self.assertEqual(body, SEGMENT_BODY * 20)

    def test_wrong_token_is_403(self):
        parts = self.proxy_url.split("/")
        parts[4] = "not-the-real-token"
        bad_url = "/".join(parts)
        with self.assertRaises(urllib.error.HTTPError) as ctx:
            urllib.request.urlopen(bad_url, timeout=5)
        self.assertEqual(ctx.exception.code, 403)

    def test_host_outside_manifest_is_403(self):
        parts = self.proxy_url.split("/")
        parts[6] = "evil.example.com"  # scheme=parts[5], host=parts[6]
        evil_url = "/".join(parts)
        with self.assertRaises(urllib.error.HTTPError) as ctx:
            urllib.request.urlopen(evil_url, timeout=5)
        self.assertEqual(ctx.exception.code, 403)

    def test_range_request_is_forwarded_to_origin(self):
        mpd = urllib.request.urlopen(self.proxy_url, timeout=5).read().decode("utf-8")
        base = re.search(r"<BaseURL>(.*?)</BaseURL>", mpd).group(1)
        from urllib.parse import urljoin
        seg_url = urljoin(base, "chunk_1.m4s")

        req = urllib.request.Request(seg_url, headers={"Range": "bytes=10-19"})
        resp = urllib.request.urlopen(req, timeout=5)
        self.assertEqual(resp.status, 206)
        self.assertEqual(resp.read(), SEGMENT_BODY[10:20])
        self.assertEqual(resp.headers.get("Content-Range"), f"bytes 10-19/{len(SEGMENT_BODY)}")

    def test_redirect_to_an_internal_address_is_not_followed(self):
        _InternalHandler.hits = 0
        with self.assertRaises(urllib.error.HTTPError) as ctx:
            urllib.request.urlopen(self._proxied("/redirect-internal"), timeout=5)
        self.assertEqual(ctx.exception.code, 403)
        self.assertEqual(_InternalHandler.hits, 0)

    def test_redirect_to_an_allowed_host_still_reaches_the_rewritten_manifest(self):
        mpd = urllib.request.urlopen(self._proxied("/moved.mpd"), timeout=5).read().decode("utf-8")
        self.assertIn("<BaseURL>", mpd)

    def test_redirect_loop_is_cut_off(self):
        with self.assertRaises(urllib.error.HTTPError) as ctx:
            urllib.request.urlopen(self._proxied("/loop-a"), timeout=10)
        self.assertEqual(ctx.exception.code, 502)

    def test_closing_stdin_shuts_the_proxy_down(self):
        self.proxy.stdin.close()
        try:
            code = self.proxy.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.proxy.kill()
            self.fail("proxy did not exit after stdin closed")
        self.assertEqual(code, 0)


if __name__ == "__main__":
    unittest.main()
