import sys
import os
import shutil
import argparse
import subprocess
import time
import datetime
import re
from urllib.parse import urljoin, urlparse

# Enable ANSI escape sequences on Windows CMD
if os.name == "nt":
    os.system("")


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


try:
    from curl_cffi import requests
except ImportError:
    Logger.error("Missing dependency: curl_cffi")
    print(f"    Current Python = {sys.executable}")
    print("    Install with:  pip install curl_cffi")
    print("    Or:            python -m pip install curl_cffi")
    sys.exit(2)


def parse_args():
    p = argparse.ArgumentParser(description="Download Cloudflare-protected m3u8 via curl_cffi")
    p.add_argument("url", help="m3u8 URL")
    p.add_argument("-o", "--output", default="output.mp4", help="Output filename (default: output.mp4)")
    p.add_argument("--cookie", default=None,
                   help="Full Cookie header, e.g. 'cf_clearance=...; __cf_bm=...'")
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

    # Segment temp directory: default next to this script (doesn't pollute user save dir)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    seg_dir = a.seg_dir if a.seg_dir else os.path.join(script_dir, "cf_segments")
    seg_dir = os.path.abspath(seg_dir)
    os.makedirs(seg_dir, exist_ok=True)

    s = requests.Session(impersonate=a.impersonate)
    headers = {"Referer": referer, "Accept": "*/*"}
    if a.cookie:
        headers["Cookie"] = a.cookie

    print(f"\n{Colors.BOLD}{Colors.CYAN}N_m3u8DL-RE{Colors.RESET} {Colors.DIM}(Cloudflare TLS Bypass Extension v2.1.3){Colors.RESET}")
    print(f"{Colors.DIM}{'=' * 75}{Colors.RESET}")
    Logger.info(f"Fingerprint Impersonation = {Colors.BOLD}{a.impersonate}{Colors.RESET}")
    Logger.info(f"Target URL                 = {Colors.WHITE}{a.url}{Colors.RESET}")
    Logger.info(f"Referer Header             = {Colors.WHITE}{referer}{Colors.RESET}")
    Logger.info(f"CF Cookie Provided         = {'Yes' if a.cookie else 'No'}")
    Logger.info(f"Segment Temp Dir           = {seg_dir}")
    Logger.info(f"Final Output Dir           = {out_dir}")
    Logger.info(f"Post-Merge Cleanup         = {'Keep (keep-segs)' if a.keep_segs else 'Auto-Delete'}")
    print(f"{Colors.DIM}{'=' * 75}{Colors.RESET}\n")

    try:
        r = s.get(a.url, headers=headers, timeout=30)
    except Exception as e:
        Logger.error(f"Request failed: {e}")
        sys.exit(1)

    if r.status_code != 200:
        Logger.error(f"M3U8 Playlist fetch status = {r.status_code}")
        print_error_box(r.status_code, a.url, referer, bool(a.cookie))
        sys.exit(1)

    Logger.info("Content Matched: HTTP Live Streaming")

    # Master Playlist handling: Check if this is a master playlist containing #EXT-X-STREAM-INF
    if "#EXT-X-STREAM-INF" in r.text:
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
                if r.status_code != 200:
                    Logger.error(f"Failed to fetch selected sub-playlist, status={r.status_code}")
                    sys.exit(1)
            except Exception as e:
                Logger.error(f"Sub-playlist request failed: {e}")
                sys.exit(1)

    # Check for Encryption (#EXT-X-KEY)
    if "#EXT-X-KEY" in r.text:
        Logger.warn("Encryption detected (#EXT-X-KEY in playlist).")
        Logger.warn("curl_cffi downloads raw segments. If playback fails, use N_m3u8DL-RE directly with --key options.")

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

    out_name = a.output if a.output.lower().endswith(".mp4") else a.output + ".mp4"
    Logger.info(f"Save Name: {out_name}")
    Logger.info("Start downloading...")

    ts = []
    max_retries = 5
    for i, u in enumerate(segs):
        d = os.path.join(seg_dir, f"{i:05d}.ts")
        download_success = False
        for attempt in range(1, max_retries + 1):
            try:
                rr = s.get(u, headers=headers, timeout=60)
                if rr.status_code == 200:
                    with open(d, "wb") as f:
                        f.write(rr.content)
                    ts.append(d)
                    download_success = True
                    # Probe media info on segment 1 download success (matching N_m3u8DL-RE!)
                    if i == 0:
                        probe_media_info(d, ff)
                    break
                else:
                    err_msg = f"status={rr.status_code}"
                    Logger.warn(f"Segment {i + 1}/{len(segs)} attempt {attempt}/{max_retries} {err_msg}")
            except Exception as e:
                raw_err = str(e)
                # Shorten long libcurl timeout messages
                if "timed out" in raw_err.lower():
                    err_msg = "timeout (60s)"
                else:
                    err_msg = raw_err[:50]
                Logger.warn(f"Segment {i + 1}/{len(segs)} attempt {attempt}/{max_retries} error: {err_msg}")
            time.sleep(1)

        if not download_success:
            Logger.error(f"Segment {i + 1} failed after {max_retries} attempts. Proceeding to merge downloaded segments...")
            break

        print_progress(i + 1, len(segs))

    Logger.info(f"Downloaded {len(ts)}/{len(segs)} segment(s)")
    if not ts:
        Logger.error("No segments were downloaded successfully.")
        sys.exit(1)

    lst = os.path.join(seg_dir, "list.txt")
    with open(lst, "w", encoding="utf-8") as f:
        for t in ts:
            f.write("file '" + os.path.abspath(t).replace("\\", "/") + "'\n")

    out_path = os.path.join(out_dir, out_name)

    Logger.info(f"Merging segments with FFmpeg -> {out_path}")
    try:
        subprocess.run(
            [ff, "-y", "-f", "concat", "-safe", "0", "-i", lst, "-c", "copy", out_path],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        Logger.info(f"{Colors.BOLD}{Colors.GREEN}Merge complete! Saved output: {out_path}{Colors.RESET}")
        if a.keep_segs:
            Logger.info(f"Segment directory kept (--keep-segs): {seg_dir}")
        else:
            try:
                shutil.rmtree(seg_dir, ignore_errors=True)
                Logger.info(f"Cleaned up segment temp directory: {seg_dir}")
            except Exception as e:
                Logger.warn(f"Cleanup failed (can delete manually): {e}")
    except FileNotFoundError:
        Logger.error("FFmpeg not found. Segments kept; merge manually:")
        print(f'    {ff} -f concat -safe 0 -i "{lst}" -c copy "{out_path}"')
    except subprocess.CalledProcessError as e:
        Logger.error(f"FFmpeg merge failed: {e}")
        Logger.warn(f"Segments kept in: {seg_dir}")


if __name__ == "__main__":
    main()
