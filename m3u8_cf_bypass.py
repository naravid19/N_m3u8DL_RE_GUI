import sys
import os
import shutil
import argparse
import subprocess
import time
import datetime
import re
import concurrent.futures
import threading
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import secrets
import errno
import hmac
import ipaddress
import socket
import urllib.parse
from urllib.parse import urljoin, urlparse, urlsplit, urlunsplit

class Colors:
    RESET = "\033[0m"
    BOLD = "\033[1m"
    DIM = "\033[2m"
    UNDERLINE = "\033[4m"
    GREEN = "\033[92m"
    YELLOW = "\033[93m"
    RED = "\033[91m"
    CYAN = "\033[96m"
    WHITE = "\033[97m"


class Logger:
    in_progress_line = False

    @staticmethod
    def _time():
        return datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]

    @classmethod
    def _check_line_reset(cls):
        if cls.in_progress_line:
            sys.stdout.write("\n")
            sys.stdout.flush()
            cls.in_progress_line = False

    @classmethod
    def info(cls, msg):
        cls._check_line_reset()
        print(f"{Colors.DIM}{cls._time()}{Colors.RESET} {Colors.UNDERLINE}{Colors.GREEN}INFO{Colors.RESET}  : {msg}")

    @classmethod
    def warn(cls, msg):
        cls._check_line_reset()
        print(f"{Colors.DIM}{cls._time()}{Colors.RESET} {Colors.UNDERLINE}{Colors.YELLOW}WARN{Colors.RESET}  : {msg}")

    @classmethod
    def error(cls, msg):
        cls._check_line_reset()
        print(f"{Colors.DIM}{cls._time()}{Colors.RESET} {Colors.UNDERLINE}{Colors.RED}ERROR{Colors.RESET} : {msg}")

    @classmethod
    def debug(cls, msg):
        cls._check_line_reset()
        print(f"{Colors.DIM}{cls._time()}{Colors.RESET} {Colors.UNDERLINE}{Colors.DIM}DEBUG{Colors.RESET} : {msg}")


def _enable_ansi():
    """Enable ANSI escape sequences on Windows CMD."""
    if os.name == "nt":
        os.system("")


def _load_http():
    """Dynamically import curl_cffi when running as script."""
    try:
        from curl_cffi import requests
        return requests
    except ImportError:
        Logger.error("Missing dependency: curl_cffi")
        print(f"    Current Python = {sys.executable}")
        print("    Install with:  pip install curl_cffi")
        print("    Or:            python -m pip install curl_cffi")
        sys.exit(2)


_thread_local = threading.local()


def _get_worker_session(make_session):
    if not hasattr(_thread_local, "session"):
        _thread_local.session = make_session()
    return _thread_local.session


def build_headers(referer=None, cookie=None, user_agent=None, custom_headers=None):
    """
    Builds the combined headers dictionary with precedence:
    Built-in defaults (Accept) -> --referer / --cookie / --user-agent -> -H custom headers.
    Header names compare case-insensitively while preserving last-set values.
    """
    headers = {"Accept": "*/*"}
    if referer:
        headers["Referer"] = referer
    if user_agent:
        headers["User-Agent"] = user_agent
    if cookie:
        headers["Cookie"] = cookie

    if custom_headers:
        for h in custom_headers:
            if not h or ":" not in h:
                continue
            name, val = h.split(":", 1)
            name = name.strip()
            val = val.strip()
            if name:
                # Case-insensitive update: remove existing match if case differs
                for existing_name in list(headers.keys()):
                    if existing_name.lower() == name.lower():
                        del headers[existing_name]
                headers[name] = val

    return headers


def headers_for_redirect(headers, from_url, to_url):
    """
    The headers to send on a redirect hop. Like curl, Cookie and Authorization are
    not carried to a different scheme, host or port; everything else is.
    """
    a, b = urlsplit(from_url), urlsplit(to_url)
    if (a.scheme, a.netloc.lower()) == (b.scheme, b.netloc.lower()):
        return headers
    return {k: v for k, v in headers.items() if k.lower() not in ("cookie", "authorization")}


def check_encryption(playlist_text):
    """
    Checks for encryption in HLS playlist (#EXT-X-KEY).
    Returns False if unencrypted or METHOD=NONE, True if encrypted with a real key.
    """
    if "#EXT-X-KEY" not in playlist_text:
        return False

    for line in playlist_text.splitlines():
        line = line.strip()
        if line.startswith("#EXT-X-KEY"):
            match = re.search(r'METHOD=([^,\s]+)', line)
            if match:
                method = match.group(1).strip().strip('"').upper()
                if method != "NONE":
                    return True
            else:
                return True
    return False


def direct_engine_gap(master_text, media_text):
    """
    Names the first HLS feature the direct downloader cannot honour, or returns None.

    The direct path concatenates every URI line of one media playlist, so an init
    segment (#EXT-X-MAP), a byte-ranged segment (#EXT-X-BYTERANGE) or a separate
    audio/subtitle rendition (#EXT-X-MEDIA with a URI) would be dropped or mangled
    and produce a silent or unplayable file. The proxy engine hands those to
    N_m3u8DL-RE, which handles them.
    """
    for line in (master_text or "").splitlines():
        if line.startswith("#EXT-X-MEDIA:") and 'URI="' in line:
            return "a separate rendition (#EXT-X-MEDIA)"
    for tag, name in (("#EXT-X-MAP", "an init segment (#EXT-X-MAP)"),
                      ("#EXT-X-BYTERANGE", "byte-ranged segments (#EXT-X-BYTERANGE)")):
        if tag in (media_text or ""):
            return name
    return None


def download_segments(make_session, segs, seg_dir, headers, workers=8, on_progress=None, retry_delay=0.5):
    """
    Downloads segments concurrently using ThreadPoolExecutor with resume support.
    Pre-existing segments in seg_dir are verified and skipped.
    Each worker thread gets its own session via threading.local().
    Returns (contiguous_count, index_to_path_map).
    """
    os.makedirs(seg_dir, exist_ok=True)
    results = {}
    completed_lock = threading.Lock()
    completed_count = 0
    max_retries = 5

    # Check for already downloaded segments to resume seamlessly
    for idx in range(len(segs)):
        dest = os.path.join(seg_dir, f"{idx:05d}.ts")
        if os.path.exists(dest) and os.path.getsize(dest) > 0:
            results[idx] = dest
            completed_count += 1

    if completed_count > 0:
        Logger.info(f"Resuming download: {completed_count}/{len(segs)} segment(s) already exist on disk.")
        if on_progress:
            on_progress(completed_count, len(segs))

    def download_one(idx, url):
        nonlocal completed_count
        dest = os.path.join(seg_dir, f"{idx:05d}.ts")
        tmp = dest + ".tmp"

        # Check if already present
        if idx in results and os.path.exists(dest) and os.path.getsize(dest) > 0:
            return True

        session = _get_worker_session(make_session)

        for attempt in range(1, max_retries + 1):
            try:
                resp = session.get(url, headers=headers, timeout=60, stream=True)
                if resp.status_code == 200:
                    written = 0
                    with open(tmp, "wb") as f:
                        for chunk in resp.iter_content(chunk_size=65536):
                            if chunk:
                                f.write(chunk)
                                written += len(chunk)
                    # A body cut off mid-transfer can end without an error. Renamed into
                    # place, it would pass the resume scan as a finished segment forever.
                    # An encoded body can't be checked: curl decodes it, but
                    # Content-Length counts the encoded bytes. A header sent twice
                    # arrives as "7, 7".
                    expected = (resp.headers.get("Content-Length") or "").split(",")[0].strip()
                    if expected.isdecimal() and not resp.headers.get("Content-Encoding") and written != int(expected):
                        raise OSError(f"truncated at {written} of {expected} bytes")
                    os.replace(tmp, dest)
                    with completed_lock:
                        completed_count += 1
                        results[idx] = dest
                        if on_progress:
                            on_progress(completed_count, len(segs))
                    return True
                else:
                    Logger.warn(f"Segment {idx + 1}/{len(segs)} attempt {attempt}/{max_retries} status={resp.status_code}")
            except OSError as e:
                if getattr(e, "errno", None) == errno.ENOSPC or "no space" in str(e).lower():
                    Logger.error(f"Disk full downloading segment {idx + 1}: {e}")
                    raise
                err_msg = str(e)[:50]
                Logger.warn(f"Segment {idx + 1}/{len(segs)} attempt {attempt}/{max_retries} IO error: {err_msg}")
            except Exception as e:
                err_msg = "timeout (60s)" if "timed out" in str(e).lower() else str(e)[:50]
                Logger.warn(f"Segment {idx + 1}/{len(segs)} attempt {attempt}/{max_retries} error: {err_msg}")
            finally:
                if os.path.exists(tmp):
                    try:
                        os.remove(tmp)
                    except Exception:
                        pass
            if retry_delay > 0:
                time.sleep(retry_delay)

        Logger.error(f"Segment {idx + 1} failed after {max_retries} attempts.")
        return False

    missing_indices = [i for i in range(len(segs)) if i not in results]
    if missing_indices:
        with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
            futures = {executor.submit(download_one, i, segs[i]): i for i in missing_indices}
            for future in concurrent.futures.as_completed(futures):
                future.result()

    # Calculate contiguous prefix from index 0
    contiguous_count = 0
    while contiguous_count in results and os.path.exists(results[contiguous_count]):
        contiguous_count += 1

    return contiguous_count, results


MANIFEST_EXTENSIONS = (".m3u8", ".m3u", ".mpd", ".ism", ".isml")

# Route shape: /u/<token>/<scheme>/<host>/<path...>?<query>
#
# A query-param route (/proxy?url=<encoded>) was tried first and broken by
# design: DASH SegmentTemplate carries $Number$/$Time$ placeholders that RE
# substitutes *after* resolving against <BaseURL>. urljoin() on a URL whose
# path holds a query string replaces the whole query, so
# "…/proxy?url=…&token=…" joined with "chunk_$Number$.m4s" produced
# "…/chunk_$Number$.m4s" — token and destination both gone. Mirroring the
# real path in the route's own path (not its query) makes urljoin resolve
# correctly, because relative references only ever touch the path.
def make_proxy_url(proxy_base, target_url, token):
    parsed = urlparse(target_url)
    path = parsed.path if parsed.path else "/"
    url = f"{proxy_base}/u/{token}/{parsed.scheme}/{parsed.netloc}{path}"
    if parsed.query:
        url += f"?{parsed.query}"
    return url


def decode_proxy_request(raw_path, expected_token):
    """
    Reverses make_proxy_url from an incoming request's raw path+query.
    Returns (target_url, None) on success, or (None, error_message) on a
    malformed route or a token mismatch.
    """
    parsed = urllib.parse.urlsplit(raw_path)
    parts = parsed.path.split("/")
    # ['', 'u', token, scheme, host, ...rest]
    if len(parts) < 5 or parts[1] != "u":
        return None, "Not a proxy route"
    # Constant-time, on bytes: compare_digest refuses a non-ASCII str, which a path can hold.
    if not hmac.compare_digest(parts[2].encode("utf-8"), expected_token.encode("utf-8")):
        return None, "Invalid or missing token"

    scheme, host = parts[3], parts[4]
    if scheme not in ("http", "https") or not host:
        return None, "Malformed proxy route"

    rest = "/" + "/".join(parts[5:])
    target = f"{scheme}://{host}{rest}"
    if parsed.query:
        target += f"?{parsed.query}"
    return target, None


def looks_like_manifest_url(url):
    path = urlparse(url).path.lower()
    return path.endswith(MANIFEST_EXTENSIONS) or path.endswith("/manifest")


def sniff_manifest_kind(body_text):
    """Content-based fallback for manifest URLs that carry no recognisable extension."""
    head = body_text[:4096]
    if "#EXTM3U" in head:
        return "hls"
    if "<MPD" in head:
        return "dash"
    if "<SmoothStreamingMedia" in head:
        return "mss"
    return None


def _is_internal_host(hostname):
    """True for an address that is not on the public internet (loopback, private, link-local...)."""
    host = (hostname or "").rstrip(".")
    # curl percent-decodes and IDN-maps a host before connecting (127.0.0.%31,
    # full-width digits). Matching its Unicode tables exactly isn't practical, so a
    # host that isn't plain ASCII as written is refused; real CDN hosts are.
    if "%" in host or not host.isascii():
        return True
    if host == "localhost" or host.endswith(".localhost"):
        return True
    try:
        ip = ipaddress.ip_address(host)
    except ValueError:
        try:
            # curl also accepts the legacy IPv4 spellings: 127.1, 2130706433, 0x7f.1
            ip = ipaddress.IPv4Address(socket.inet_aton(host))
        except (OSError, ValueError):
            # ponytail: literal addresses only. A DNS name that resolves to an
            # internal address still passes; pin resolution (CURLOPT_RESOLVE) if
            # that threat matters.
            return False
    return not ip.is_global


def _track_host(allowed_hosts, url):
    """
    Admits url's host to allowed_hosts. Only the user-supplied origin (seeded by
    run_proxy_server) may be internal: a host named by a manifest or a redirect is
    attacker-influenced, so an internal address found here is never admitted.
    """
    if allowed_hosts is not None:
        parsed = urlsplit(url)
        if not _is_internal_host(parsed.hostname):
            allowed_hosts.add(parsed.netloc.lower())


def rewrite_hls_manifest(base_url, text, proxy_base, token, allowed_hosts=None):
    """
    Rewrites an HLS playlist (master or media) so that:
    - #EXT-X-STREAM-INF variant URLs
    - #EXT-X-KEY URI="..." values
    - Segment URLs
    route through the local proxy. Every host a rewritten URL resolves to is
    added to allowed_hosts (when given), so a manifest legitimately spanning
    several CDN hosts does not get 403'd fetching its own segments. Internal
    addresses are the exception (see _track_host).
    """
    lines = text.splitlines()
    out = []

    for line in lines:
        stripped = line.strip()
        if not stripped:
            out.append(line)
            continue

        if stripped.startswith("#EXT-X-STREAM-INF"):
            out.append(line)
            continue

        if stripped.startswith(("#EXT-X-KEY", "#EXT-X-MAP", "#EXT-X-MEDIA", "#EXT-X-PRELOAD-HINT", "#EXT-X-SESSION-KEY", "#EXT-X-PART",
                                "#EXT-X-I-FRAME-STREAM-INF", "#EXT-X-RENDITION-REPORT")):
            def replace_key_uri(match):
                uri_val = match.group(1)
                full_uri = urljoin(base_url, uri_val)
                _track_host(allowed_hosts, full_uri)
                return f'URI="{make_proxy_url(proxy_base, full_uri, token)}"'

            new_line = re.sub(r'URI="([^"]+)"', replace_key_uri, stripped)
            out.append(new_line)
            continue

        if stripped.startswith("#"):
            out.append(line)
            continue

        # Non-comment line (stream variant or segment)
        full_url = urljoin(base_url, stripped)
        _track_host(allowed_hosts, full_url)
        out.append(make_proxy_url(proxy_base, full_url, token))

    return "\n".join(out)


def rewrite_dash_manifest(mpd_url, text, proxy_base, token, allowed_hosts=None):
    """
    Rewrites an MPD DASH manifest.
    If <BaseURL> is present, rewrites each <BaseURL> value through proxy.
    If no <BaseURL> exists, injects a <BaseURL> right after <MPD ...> tag.
    SegmentTemplate/@media and friends are deliberately left untouched: they
    are templates containing $Number$/$Time$ placeholders, not URLs, and
    resolve against <BaseURL> (or, absent one, this manifest's own proxied
    request URL) at request time.
    """
    parsed_mpd = urlsplit(mpd_url)
    dir_path = parsed_mpd.path[:parsed_mpd.path.rfind("/") + 1] if "/" in parsed_mpd.path else parsed_mpd.path
    base_dir = urlunsplit((parsed_mpd.scheme, parsed_mpd.netloc, dir_path, parsed_mpd.query, ""))

    if "<BaseURL>" in text:
        def replace_base_url(match):
            val = match.group(1).strip()
            full_val = urljoin(mpd_url, val)
            if "?" not in val and parsed_mpd.query:
                p = urlsplit(full_val)
                full_val = urlunsplit((p.scheme, p.netloc, p.path, parsed_mpd.query, p.fragment))
            _track_host(allowed_hosts, full_val)
            return f"<BaseURL>{make_proxy_url(proxy_base, full_val, token)}</BaseURL>"

        return re.sub(r'<BaseURL>(.*?)</BaseURL>', replace_base_url, text)

    _track_host(allowed_hosts, base_dir)
    proxied_base_dir = make_proxy_url(proxy_base, base_dir, token)

    def inject_base_url(match):
        mpd_tag = match.group(0)
        return f"{mpd_tag}\n  <BaseURL>{proxied_base_dir}</BaseURL>"

    return re.sub(r'<MPD[^>]*>', inject_base_url, text, count=1)


def rewrite_mss_manifest(manifest_url, text, proxy_base, token, allowed_hosts=None):
    """
    Smooth Streaming manifests carry no BaseURL-equivalent: StreamIndex/@Url
    is a relative fragment-request template ("QualityLevels({bitrate})/...")
    resolved by the client against the manifest's own request URL. Because
    that request URL is already the proxied one (the client fetched the
    manifest through this proxy), the template resolves correctly with no
    rewriting — the same host is already in allowed_hosts as the manifest's
    own host. This function exists only to make that a documented, tested
    decision rather than an accidental omission.
    """
    _track_host(allowed_hosts, manifest_url)
    return text


def run_proxy_server(target_url, headers, impersonate="chrome", port=0, idle_timeout=None):
    """
    Starts ThreadingHTTPServer on 127.0.0.1:port with a random per-run auth
    token. Every route is /u/<token>/<scheme>/<host>/<path>; a manifest-shaped
    response is fetched, rewritten and re-served as text, anything else is
    streamed through unchanged with Range support. Redirects are followed here,
    hop by hop. Hosts are allowed once a manifest this run served or a redirect
    it followed names them, unless they are an internal address (SSRF guard) —
    the origin host is seeded up front so the very first manifest fetch succeeds.
    Logs PROXY_READY and exits on stdin EOF or idle_timeout seconds with no
    request, so the process cannot be orphaned if the caller dies.
    """
    token = secrets.token_hex(16)
    allowed_hosts = {urlparse(target_url).netloc.lower()}
    last_activity = [time.time()]

    class ProxyHandler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"  # keep-alive: lets one worker thread's
        # curl_cffi session (thread-local) serve many segments over one TCP
        # connection instead of a fresh TLS handshake per file.

        def log_message(self, format, *args):
            pass

        def _forbidden(self, message):
            body = message.encode("utf-8")
            self.send_response(403)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            last_activity[0] = time.time()
            target, err = decode_proxy_request(self.path, token)
            if err:
                self._forbidden(f"Forbidden: {err}\n")
                return

            dest_host = urlparse(target).netloc.lower()
            if dest_host not in allowed_hosts:
                self._forbidden("Forbidden: Host not admitted by this proxy\n")
                return

            proxy_base = f"http://127.0.0.1:{self.server.server_port}"
            session = _get_worker_session(lambda: _load_http().Session(impersonate=impersonate))

            req_headers = dict(headers)
            client_range = self.headers.get("Range")
            if client_range:
                req_headers["Range"] = client_range

            try:
                # Redirects are followed here one hop at a time, at most 30 (curl's own
                # default), so every hop is admitted exactly like a URL named in a manifest.
                for _ in range(30):
                    resp = session.get(target, headers=req_headers, timeout=60, stream=True, allow_redirects=False)
                    location = resp.headers.get("Location")
                    if not (300 <= resp.status_code < 400 and location):
                        break
                    resp.close()
                    next_url = urljoin(target, location)
                    req_headers = headers_for_redirect(req_headers, target, next_url)
                    target = next_url
                    _track_host(allowed_hosts, target)
                    hop = urlsplit(target)
                    if hop.scheme not in ("http", "https") or hop.netloc.lower() not in allowed_hosts:
                        self._forbidden("Forbidden: Redirect to a host not admitted by this proxy\n")
                        return
                else:
                    raise RuntimeError("Too many redirects")
            except Exception as e:
                self.send_response(502)
                body = str(e).encode("utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return

            try:
                is_manifest = False
                if resp.status_code == 200 and not client_range:
                    if looks_like_manifest_url(target):
                        is_manifest = True
                    else:
                        ctype = (resp.headers.get("Content-Type") or "").lower()
                        if "mpegurl" in ctype or "dash+xml" in ctype or "sstr+xml" in ctype:
                            is_manifest = True

                if is_manifest:
                    # stream=True means .text/.content never populate (curl_cffi
                    # only fills them from a non-streamed read) — read the body
                    # through the same iterator a segment would use instead.
                    body_bytes = b"".join(resp.iter_content(chunk_size=65536))
                    body_text = body_bytes.decode("utf-8", errors="replace")
                    kind = sniff_manifest_kind(body_text)
                    if target.lower().split("?")[0].endswith((".mpd",)) or kind == "dash":
                        rewritten = rewrite_dash_manifest(target, body_text, proxy_base, token, allowed_hosts)
                        ctype = "application/dash+xml"
                    elif kind == "mss" or target.lower().split("?")[0].endswith((".ism", ".isml")):
                        rewritten = rewrite_mss_manifest(target, body_text, proxy_base, token, allowed_hosts)
                        ctype = "application/vnd.ms-sstr+xml"
                    else:
                        rewritten = rewrite_hls_manifest(target, body_text, proxy_base, token, allowed_hosts)
                        ctype = "application/vnd.apple.mpegurl"

                    data = rewritten.encode("utf-8")
                    self.send_response(200)
                    self.send_header("Content-Type", ctype)
                    self.send_header("Content-Length", str(len(data)))
                    self.end_headers()
                    self.wfile.write(data)
                    return

                self.send_response(resp.status_code)
                # curl_cffi de-chunks and decodes the body it hands us, so the origin's
                # Content-Length only describes that body when the reply was neither
                # encoded nor chunked. Otherwise the size is unknown here: end the body by
                # closing the connection rather than promise bytes that never arrive
                # (keep-alive would leave the client waiting) or cut a longer decoded body.
                length_is_reliable = (
                    "Content-Length" in resp.headers
                    and "Content-Encoding" not in resp.headers
                    and "Transfer-Encoding" not in resp.headers
                )
                for name in ("Content-Type", "Content-Range", "Accept-Ranges"):
                    if name in resp.headers:
                        self.send_header(name, resp.headers[name])
                if length_is_reliable:
                    self.send_header("Content-Length", resp.headers["Content-Length"])
                else:
                    self.send_header("Connection", "close")
                    self.close_connection = True
                self.end_headers()
                for chunk in resp.iter_content(chunk_size=65536):
                    if chunk:
                        self.wfile.write(chunk)
            except Exception as e:
                Logger.debug(f"Proxy stream error for {target}: {e}")

    server = ThreadingHTTPServer(("127.0.0.1", port), ProxyHandler)
    assigned_port = server.server_port
    manifest_proxy_url = make_proxy_url(f"http://127.0.0.1:{assigned_port}", target_url, token)

    Logger.info(f"{Colors.BOLD}{Colors.GREEN}Local Cloudflare Proxy Server started on 127.0.0.1:{assigned_port}{Colors.RESET}")
    print(f"PROXY_READY {manifest_proxy_url}", flush=True)

    def watch_stdin():
        try:
            sys.stdin.readline()
        except Exception:
            pass
        server.shutdown()

    def watch_idle():
        if idle_timeout is None:
            return
        while True:
            time.sleep(5)
            if time.time() - last_activity[0] > idle_timeout:
                server.shutdown()
                return

    threading.Thread(target=watch_stdin, daemon=True).start()
    if idle_timeout is not None:
        threading.Thread(target=watch_idle, daemon=True).start()

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


def parse_args():
    p = argparse.ArgumentParser(description="Download Cloudflare-protected m3u8 via curl_cffi")
    p.add_argument("url", help="m3u8 URL")
    p.add_argument("-o", "--output", default="output.mp4", help="Output filename (default: output.mp4)")
    p.add_argument("--cookie", default=None,
                   help="Full Cookie header, e.g. 'cf_clearance=...; __cf_bm=...'")
    p.add_argument("--user-agent", default=None,
                   help="User-Agent header to send with requests")
    p.add_argument("-H", "--header", action="append", default=[],
                   help="Custom header in 'Name: Value' format (can be specified multiple times)")
    p.add_argument("--impersonate", default="chrome",
                   help="Browser fingerprint: chrome / chrome120 / chrome131 / edge101 / safari17_0")
    p.add_argument("--referer", default=None,
                   help="Referer (player page URL). Default: auto-derived from input URL domain")
    p.add_argument("--work-dir", default=".",
                   help="Final mp4 save directory (GUI main Save Directory)")
    p.add_argument("--seg-dir", default=None,
                   help="Segment temp directory (default: cf_segments next to this script)")
    p.add_argument("--keep-segs", action="store_true",
                   help="Keep segment directory after successful merge; default is to auto-delete")
    p.add_argument("--threads", type=int, default=0,
                   help="Concurrent segment download threads (default: min(16, cpu*4))")
    p.add_argument("--serve", action="store_true",
                   help="Run local HTTP manifest/segment reverse proxy server for DASH/MSS/CENC")
    return p.parse_args()


def parse_master_playlist(master_url, text):
    """
    Parses #EXT-X-STREAM-INF variants from an M3U8 Master Playlist.
    Returns a list of dicts containing resolution, bandwidth, framerate, codecs, and URL.
    """
    lines = text.splitlines()
    variants = []

    for i, line in enumerate(lines):
        line = line.strip()
        if line.startswith("#EXT-X-STREAM-INF"):
            attrs = line[18:]

            # Extract BANDWIDTH
            bw = 0
            bw_match = re.search(r'BANDWIDTH=(\d+)', attrs)
            if bw_match:
                bw = int(bw_match.group(1))

            if bw >= 1_000_000:
                bw_str = f"{bw / 1_000_000:.2f} Mbps"
            elif bw > 0:
                bw_str = f"{bw // 1000} Kbps"
            else:
                bw_str = "Unknown Kbps"

            # Extract RESOLUTION
            res_str = ""
            res_match = re.search(r'RESOLUTION=([\dxX]+)', attrs)
            if res_match:
                res_str = res_match.group(1)

            # Extract FRAME-RATE
            fps_str = ""
            fps_match = re.search(r'FRAME-RATE=([\d\.]+)', attrs)
            if fps_match:
                try:
                    fps_val = float(fps_match.group(1))
                    fps_str = f"{int(fps_val) if fps_val.is_integer() else fps_val} fps"
                except ValueError:
                    pass

            # Extract CODECS
            codecs_str = ""
            codecs_match = re.search(r'CODECS="([^"]+)"', attrs)
            if codecs_match:
                codecs_str = codecs_match.group(1)

            # Find next non-comment URL line
            for j in range(i + 1, len(lines)):
                next_line = lines[j].strip()
                if next_line and not next_line.startswith("#"):
                    sub_url = urljoin(master_url, next_line)
                    variants.append({
                        "url": sub_url,
                        "bandwidth": bw,
                        "bandwidth_str": bw_str,
                        "resolution": res_str,
                        "framerate": fps_str,
                        "codecs": codecs_str,
                    })
                    break

    # Sort variants by bandwidth descending
    variants.sort(key=lambda x: x["bandwidth"], reverse=True)
    return variants


def format_variant_info(v):
    parts = ["Vid"]
    if v["resolution"]:
        parts.append(v["resolution"])
    parts.append(v["bandwidth_str"])
    if v["framerate"]:
        parts.append(v["framerate"])
    if v["codecs"]:
        parts.append(v["codecs"])
    return " | ".join(parts)


def derive_referer(url):
    """Auto-derive scheme://netloc/ from input URL to use as default Referer."""
    try:
        parsed = urlparse(url)
        if parsed.scheme and parsed.netloc:
            return f"{parsed.scheme}://{parsed.netloc}/"
    except Exception:
        pass
    return url.rsplit("/", 1)[0] + "/" if "/" in url else url


def output_path(out_dir, output):
    """
    Where the merged file goes: always inside out_dir. -o names a file, not a path;
    os.path.join would otherwise drop out_dir for an absolute -o, or climb out of it
    through "..".
    """
    name = os.path.basename(output.replace("\\", "/")) or "output"
    if not name.lower().endswith(".mp4"):
        name += ".mp4"
    return os.path.join(out_dir, name)


def print_progress(current, total):
    Logger.in_progress_line = True
    bar_length = 25
    filled = int(bar_length * current // total)
    bar = "█" * filled + "░" * (bar_length - filled)
    pct = (current * 100) / total
    time_str = datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]
    msg = f"\r{Colors.DIM}{time_str}{Colors.RESET} {Colors.UNDERLINE}{Colors.GREEN}INFO{Colors.RESET}  : Downloading [{Colors.CYAN}{bar}{Colors.RESET}] {current}/{total} ({pct:.1f}%)"
    sys.stdout.write(msg)
    sys.stdout.flush()
    if current == total:
        sys.stdout.write("\n")
        Logger.in_progress_line = False


def print_error_box(status_code, url, referer, has_cookie):
    Logger._check_line_reset()
    print(f"\n{Colors.DIM}{'=' * 75}{Colors.RESET}")
    Logger.error(f"{Colors.BOLD}{Colors.RED}Access Denied (HTTP {status_code} / Cloudflare Protection){Colors.RESET}")
    print(f"{Colors.DIM}{'-' * 75}{Colors.RESET}")
    print(f"  {Colors.BOLD}Request Context:{Colors.RESET}")
    print(f"    • Target URL : {url}")
    print(f"    • Referer    : {referer}")
    print(f"    • CF Cookie  : {'(Provided)' if has_cookie else '(None)'}")
    print(f"\n  {Colors.BOLD}💡 Troubleshooting Steps:{Colors.RESET}")
    print(f"    {Colors.YELLOW}1. Referer Mismatch (Most Common Fix):{Colors.RESET}")
    print("       • If downloading from a CDN (e.g. cdn.example.com), Cloudflare blocks requests")
    print("         where Referer is set to the CDN domain instead of the main website.")
    print(f"       • {Colors.CYAN}FIX:{Colors.RESET} In GUI -> 'Cloudflare Bypass' section -> set 'Referer' to the")
    print("         actual website webpage URL where you watch the video (e.g. https://example.com/).")
    print(f"\n    {Colors.YELLOW}2. Cloudflare JS Challenge / Cookie Required:{Colors.RESET}")
    print("       • If setting Referer still yields 403, Cloudflare requires a Turnstile cookie.")
    print(f"       • {Colors.CYAN}FIX:{Colors.RESET} Open video page in Chrome/Edge, press F12 (DevTools) -> Network tab,")
    print("         copy the 'Cookie:' header (cf_clearance=...), and paste into 'CF Cookie' in GUI.")
    print(f"{Colors.DIM}{'=' * 75}{Colors.RESET}\n")


def probe_media_info(seg_path, ffmpeg_cmd="ffmpeg"):
    """
    Probes segment media info using FFmpeg -hide_banner -i <seg_path>.
    Matches N_m3u8DL-RE's MediainfoUtil logic exactly!
    """
    if not seg_path or not os.path.exists(seg_path):
        return

    Logger.warn("Reading media info...")
    try:
        res = subprocess.run(
            [ffmpeg_cmd, "-hide_banner", "-i", seg_path],
            stderr=subprocess.PIPE,
            stdout=subprocess.PIPE,
            text=True,
            timeout=10,
            encoding="utf-8",
            errors="ignore"
        )
        output = res.stderr or ""

        # Regex matching Stream lines (same as N_m3u8DL-RE TextRegex)
        stream_matches = re.findall(r'Stream #.*', output)
        for stream_line in stream_matches:
            # Extract stream ID e.g. [0x100] or #0:0
            id_match = re.search(r'#0:\d(\[0x\w+\])?', stream_line)
            stream_id = id_match.group(1) if id_match and id_match.group(1) else "NaN"

            # Extract Type (Video / Audio / Subtitle) and Text
            type_match = re.search(r': (\w+): (.*)', stream_line)
            if not type_match:
                continue
            stype, stext = type_match.group(1), type_match.group(2).strip()

            # Base info
            base_info = stext.split(",")[0].strip() if "," in stext else stext
            base_info = re.sub(r' \/ 0x\w+', '', base_info)

            # Resolution, Bitrate, FPS
            res_match = re.search(r'\d{2,}x\d+', stext)
            resolution = res_match.group(0) if res_match else ""

            bitrate_match = re.search(r'\d+ kb\/s', stext)
            bitrate = bitrate_match.group(0) if bitrate_match else ""

            fps_match = re.search(r'(\d+(\.\d+)?) fps', stext)
            fps = fps_match.group(0) if fps_match else ""

            # Format output line matching N_m3u8DL-RE ToString()
            parts = [stype, base_info]
            if resolution: parts.append(resolution)
            if fps: parts.append(fps)
            if bitrate: parts.append(bitrate)

            info_str = f"{stream_id}: " + ", ".join(parts)
            if "/bt2020/" in stext:
                info_str += " [HDR]"
            if "dvhe" in base_info or "dvh1" in base_info or "DOVI" in base_info:
                info_str += " [DOVI]"

            Logger.info(f"{Colors.CYAN}{info_str}{Colors.RESET}")
    except Exception:
        pass


def main():
    _enable_ansi()
    requests = _load_http()
    a = parse_args()
    out_dir = os.path.abspath(a.work_dir)
    os.makedirs(out_dir, exist_ok=True)

    # Resolve ffmpeg executable early for media probing and merging
    ff = "ffmpeg"
    if not shutil.which(ff):
        here = os.path.dirname(os.path.abspath(__file__))
        cand = os.path.join(here, "ffmpeg.exe") if os.name == "nt" else os.path.join(here, "ffmpeg")
        if os.path.exists(cand):
            ff = cand

    # Auto-derive Referer if not explicitly passed
    referer = a.referer if a.referer else derive_referer(a.url)

    # Segment temp directory
    script_dir = os.path.dirname(os.path.abspath(__file__))
    seg_dir = a.seg_dir if a.seg_dir else os.path.join(script_dir, "cf_segments")
    seg_dir = os.path.abspath(seg_dir)
    os.makedirs(seg_dir, exist_ok=True)

    headers = build_headers(referer=referer, cookie=a.cookie, user_agent=a.user_agent, custom_headers=a.header)
    s = requests.Session(impersonate=a.impersonate)

    print(f"\n{Colors.BOLD}{Colors.CYAN}N_m3u8DL-RE{Colors.RESET} {Colors.DIM}(Cloudflare TLS Bypass Module){Colors.RESET}")
    print(f"{Colors.DIM}{'=' * 75}{Colors.RESET}")
    Logger.info(f"Fingerprint Impersonation = {Colors.BOLD}{a.impersonate}{Colors.RESET}")
    Logger.info(f"Target URL                 = {Colors.WHITE}{a.url}{Colors.RESET}")
    Logger.info(f"Referer Header             = {Colors.WHITE}{referer}{Colors.RESET}")
    Logger.info(f"CF Cookie Provided         = {'Yes (' + str(len(a.cookie)) + ' chars)' if a.cookie else 'No'}")
    Logger.info(f"Headers Count              = {len(headers)}")
    Logger.info(f"Segment Temp Dir           = {seg_dir}")
    Logger.info(f"Final Output Dir           = {out_dir}")
    Logger.info(f"Post-Merge Cleanup         = {'Keep (keep-segs)' if a.keep_segs else 'Auto-Delete'}")
    print(f"{Colors.DIM}{'=' * 75}{Colors.RESET}\n")

    if a.serve:
        run_proxy_server(a.url, headers, impersonate=a.impersonate)
        sys.exit(0)

    try:
        r = s.get(a.url, headers=headers, timeout=30)
    except Exception as e:
        Logger.error(f"Request failed: {e}")
        sys.exit(1)

    if r.status_code in (401, 403):
        Logger.error(f"M3U8 Playlist fetch status = {r.status_code}")
        print_error_box(r.status_code, a.url, referer, bool(a.cookie))
        sys.exit(5)
    elif r.status_code != 200:
        Logger.error(f"M3U8 Playlist fetch status = {r.status_code}")
        print_error_box(r.status_code, a.url, referer, bool(a.cookie))
        sys.exit(1)

    Logger.info("Content Matched: HTTP Live Streaming")

    master_text = ""

    # Master Playlist handling: Check if this is a master playlist containing #EXT-X-STREAM-INF
    if "#EXT-X-STREAM-INF" in r.text:
        master_text = r.text
        Logger.info("Parsing streams...")
        Logger.warn("Master List detected, try parse all streams")
        variants = parse_master_playlist(a.url, r.text)
        if variants:
            Logger.info(f"Extracted, there are {len(variants)} stream(s):")
            for v in variants:
                Logger.info(f"  {format_variant_info(v)}")

            best_variant = variants[0]
            Logger.info("Selected streams:")
            Logger.info(f"  {format_variant_info(best_variant)}")
            a.url = best_variant["url"]
            try:
                r = s.get(a.url, headers=headers, timeout=30)
                if r.status_code in (401, 403):
                    Logger.error(f"Failed to fetch selected sub-playlist, status={r.status_code}")
                    sys.exit(5)
                elif r.status_code != 200:
                    Logger.error(f"Failed to fetch selected sub-playlist, status={r.status_code}")
                    sys.exit(1)
            except Exception as e:
                Logger.error(f"Sub-playlist request failed: {e}")
                sys.exit(1)

    gap = direct_engine_gap(master_text, r.text)
    if gap:
        Logger.error(f"This stream uses {gap}, which the direct downloader cannot assemble.")
        Logger.error("Use the proxy engine (Cloudflare engine: Auto or Proxy) so N_m3u8DL-RE handles it.")
        sys.exit(6)

    # Check for Encryption (#EXT-X-KEY) - C4 defect
    if check_encryption(r.text):
        Logger.error("Encryption detected (#EXT-X-KEY with METHOD!=NONE in playlist).")
        Logger.error("curl_cffi cannot decrypt DRM/encrypted streams. Use N_m3u8DL-RE directly with --key.")
        sys.exit(4)

    segs = []
    for line in r.text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        segs.append(urljoin(a.url, line))
    Logger.info(f"Parsed {len(segs)} segment(s)")
    if not segs:
        Logger.error("No segments found in m3u8 playlist. Check URL or use sub-playlist.")
        sys.exit(1)

    out_path = output_path(out_dir, a.output)
    Logger.info(f"Save Name: {os.path.basename(out_path)}")
    Logger.info("Start downloading...")

    workers = a.threads if a.threads > 0 else min(16, (os.cpu_count() or 4) * 4)
    make_session = lambda: requests.Session(impersonate=a.impersonate)

    contiguous_count, results = download_segments(
        make_session, segs, seg_dir, headers, workers=workers, on_progress=print_progress
    )

    if 0 in results and os.path.exists(results[0]):
        probe_media_info(results[0], ff)

    Logger.info(f"Downloaded {contiguous_count}/{len(segs)} contiguous segment(s)")
    if contiguous_count == 0:
        Logger.error("No segments were downloaded successfully.")
        sys.exit(1)

    lst = os.path.join(seg_dir, "list.txt")
    with open(lst, "w", encoding="utf-8") as f:
        for i in range(contiguous_count):
            t = results[i]
            f.write("file '" + os.path.abspath(t).replace("\\", "/") + "'\n")


    Logger.info(f"Merging segments with FFmpeg -> {out_path}")
    try:
        subprocess.run(
            [ff, "-y", "-f", "concat", "-safe", "0", "-i", lst, "-c", "copy", out_path],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        if contiguous_count < len(segs):
            Logger.warn(f"Partial download: saved {contiguous_count}/{len(segs)} segments to {out_path}")
            # A partial run is exactly the resume state: --keep-segs governs
            # what happens to segments after a *complete* download, not
            # whether the in-progress ones survive to be resumed. Deleting
            # them here (the pre-resume behaviour) made every partial run
            # destroy its own resume point under the GUI's default (unchecked)
            # setting, so a second attempt always restarted from segment 0.
            Logger.info(f"Segment directory kept for resume: {seg_dir}")
            sys.exit(3)

        Logger.info(f"{Colors.BOLD}{Colors.GREEN}Merge complete! Saved output: {out_path}{Colors.RESET}")
        if a.keep_segs:
            Logger.info(f"Segment directory kept (--keep-segs): {seg_dir}")
        else:
            try:
                shutil.rmtree(seg_dir, ignore_errors=True)
                Logger.info(f"Cleaned up segment temp directory: {seg_dir}")
            except Exception as e:
                Logger.warn(f"Cleanup failed (can delete manually): {e}")
        sys.exit(0)
    except FileNotFoundError:
        Logger.error("FFmpeg not found. Segments kept; merge manually:")
        print(f'    {ff} -f concat -safe 0 -i "{lst}" -c copy "{out_path}"')
        sys.exit(1)
    except subprocess.CalledProcessError as e:
        Logger.error(f"FFmpeg merge failed: {e}")
        Logger.warn(f"Segments kept in: {seg_dir}")
        sys.exit(1)


if __name__ == "__main__":
    main()
