#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
m3u8_cf_bypass.py
Cloudflare-protected m3u8 downloader based on curl_cffi browser TLS fingerprint impersonation.
Called by the GUI when "Bypass Cloudflare" is checked.

Why it is needed:
  N_m3u8DL-RE uses .NET HttpClient, whose TLS handshake fingerprint (JA3/JA4) reveals
  a non-browser identity, causing Cloudflare WAF to return 403.
  curl_cffi is built on libcurl + BoringSSL and can byte-for-byte replicate Chrome's
  TLS/HTTP2 fingerprint, so Cloudflare treats the request as a real browser.

Directory layout:
  --work-dir : final merged mp4 saved here (GUI "Save Directory")
  --seg-dir  : segment (.ts) temp directory, default: cf_segments next to this script
  --keep-segs: keep segment directory after successful merge; auto-deleted otherwise

Dependencies:
  pip install curl_cffi
  ffmpeg (in PATH, or same directory as this script)
"""
import sys
import os
import shutil
import argparse
import subprocess
import time
from urllib.parse import urljoin, urlparse


try:
    from curl_cffi import requests
except ImportError:
    print("[!] Missing dependency: curl_cffi")
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


def resolve_master_playlist(master_url, text):
    lines = text.splitlines()
    best_url = None
    max_bw = -1

    for i, line in enumerate(lines):
        line = line.strip()
        if line.startswith("#EXT-X-STREAM-INF"):
            bw = 0
            for part in line.split(","):
                if "BANDWIDTH=" in part:
                    try:
                        bw = int(part.split("BANDWIDTH=")[1].split()[0])
                    except ValueError:
                        pass
            # Next non-comment line is the stream URL
            for j in range(i + 1, len(lines)):
                next_line = lines[j].strip()
                if next_line and not next_line.startswith("#"):
                    if bw > max_bw:
                        max_bw = bw
                        best_url = urljoin(master_url, next_line)
                    break

    return best_url or master_url


def derive_referer(url):
    """Auto-derive scheme://netloc/ from input URL to use as default Referer."""
    try:
        parsed = urlparse(url)
        if parsed.scheme and parsed.netloc:
            return f"{parsed.scheme}://{parsed.netloc}/"
    except Exception:
        pass
    return url.rsplit("/", 1)[0] + "/" if "/" in url else url


def main():
    a = parse_args()
    out_dir = os.path.abspath(a.work_dir)
    os.makedirs(out_dir, exist_ok=True)

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

    print(f"[*] CF Downloader | Fingerprint={a.impersonate} | Cookie={'yes' if a.cookie else 'no'}")
    print(f"[*] URL = {a.url}")
    print(f"[*] Referer = {referer}")
    print(f"[*] Segment temp dir = {seg_dir}")
    print(f"[*] Final output dir = {out_dir}")
    print(f"[*] Post-merge cleanup = {'keep (keep-segs set)' if a.keep_segs else 'auto-delete'}")

    try:
        r = s.get(a.url, headers=headers, timeout=30)
    except Exception as e:
        print(f"[!] Request failed: {e}")
        sys.exit(1)

    print(f"[m3u8] Status code = {r.status_code}")
    if r.status_code != 200:
        body = r.text or ""
        low = body.lower()
        if "attention required" in low or "you have been blocked" in low:
            print("[!] Cloudflare WAF blocked (Attention Required / You have been blocked).")
            if not a.cookie:
                print("    -> No Cookie passed: use a browser extension to get the site Cookie, then add --cookie and retry.")
            else:
                print("    -> Cookie passed but still blocked: Cookie expired or current IP is flagged.")
                print("       (Datacenter IPs are often blocked; residential IPs usually work.)")
        elif "just a moment" in low or "challenge-platform" in low or "cf-chl" in low:
            print("[!] Cloudflare JS challenge (Just a moment). Need a valid cf_clearance.")
            print("    Use a browser extension to get the Cookie, then add --cookie and retry.")
        else:
            print("[!] Non-200 response, first 300 chars:")
            print(body[:300])
        sys.exit(1)

    # Master Playlist handling: Check if this is a master playlist containing #EXT-X-STREAM-INF
    if "#EXT-X-STREAM-INF" in r.text:
        print("[*] Master playlist detected! Resolving highest quality variant stream...")
        sub_url = resolve_master_playlist(a.url, r.text)
        print(f"[*] Selected sub-playlist = {sub_url}")
        try:
            r = s.get(sub_url, headers=headers, timeout=30)
            if r.status_code != 200:
                print(f"[!] Failed to fetch sub-playlist, status={r.status_code}")
                sys.exit(1)
            # Update base url for relative segment links
            a.url = sub_url
        except Exception as e:
            print(f"[!] Sub-playlist request failed: {e}")
            sys.exit(1)

    # Check for Encryption (#EXT-X-KEY)
    if "#EXT-X-KEY" in r.text:
        print("[!] WARNING: Encryption detected (#EXT-X-KEY in playlist).")
        print("    curl_cffi downloads raw segments. If playback fails, use N_m3u8DL-RE directly with --key options.")

    segs = []
    for line in r.text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        segs.append(urljoin(a.url, line))
    print(f"[+] Parsed {len(segs)} segments")
    if not segs:
        print("[!] No segments found in m3u8 — this may be a master playlist. Use a sub-playlist URL.")
        sys.exit(1)

    ts = []
    max_retries = 5
    progress_step = 5 if len(segs) <= 50 else 10
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
                    break
                else:
                    print(f"[!] Segment {i} attempt {attempt}/{max_retries} status={rr.status_code}")
            except Exception as e:
                print(f"[!] Segment {i} attempt {attempt}/{max_retries} error: {e}")
            time.sleep(1)

        if not download_success:
            print(f"[!] Segment {i} failed after {max_retries} attempts. Proceeding to merge downloaded segments...")
            break

        if (i + 1) % progress_step == 0 or i == len(segs) - 1:
            pct = (i + 1) * 100 // len(segs)
            print(f"    Progress: {i + 1}/{len(segs)} segments ({pct}%)")

    print(f"[+] Got {len(ts)}/{len(segs)} segments")
    if not ts:
        sys.exit(1)

    lst = os.path.join(seg_dir, "list.txt")
    with open(lst, "w", encoding="utf-8") as f:
        for t in ts:
            f.write("file '" + os.path.abspath(t).replace("\\", "/") + "'\n")

    out_name = a.output if a.output.lower().endswith(".mp4") else a.output + ".mp4"
    out_path = os.path.join(out_dir, out_name)
    ff = "ffmpeg"
    if not _which(ff):
        here = os.path.dirname(os.path.abspath(__file__))
        cand = os.path.join(here, "ffmpeg.exe") if os.name == "nt" else os.path.join(here, "ffmpeg")
        if os.path.exists(cand):
            ff = cand
    try:
        subprocess.run(
            [ff, "-y", "-f", "concat", "-safe", "0", "-i", lst, "-c", "copy", out_path],
            check=True,
        )
        print(f"[+] Merge complete: {out_path}")
        if a.keep_segs:
            print(f"[*] Segment directory kept (keep-segs): {seg_dir}")
        else:
            try:
                shutil.rmtree(seg_dir, ignore_errors=True)
                print(f"[+] Cleaned up segment temp directory: {seg_dir}")
            except Exception as e:
                print(f"[!] Cleanup failed (can delete manually): {e}")
    except FileNotFoundError:
        print("[!] ffmpeg not found. Segments are kept; you can merge manually:")
        print(f'    {ff} -f concat -safe 0 -i "{lst}" -c copy "{out_path}"')
    except subprocess.CalledProcessError as e:
        print(f"[!] ffmpeg merge failed: {e}")
        print(f"    Segments kept in: {seg_dir}")


def _which(cmd):
    from shutil import which
    return which(cmd)


if __name__ == "__main__":
    main()
