import unittest
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import build_headers, headers_for_redirect


class TestHeaders(unittest.TestCase):
    def test_precedence_and_defaults(self):
        headers = build_headers(
            referer="https://example.com/",
            cookie="cf_clearance=123",
            user_agent="CustomUA/1.0",
            custom_headers=["Accept-Language: en-US", "Authorization: Bearer token"]
        )

        self.assertEqual(headers["Accept"], "*/*")
        self.assertEqual(headers["Referer"], "https://example.com/")
        self.assertEqual(headers["Cookie"], "cf_clearance=123")
        self.assertEqual(headers["User-Agent"], "CustomUA/1.0")
        self.assertEqual(headers["Accept-Language"], "en-US")
        self.assertEqual(headers["Authorization"], "Bearer token")

    def test_custom_header_overrides_case_insensitively(self):
        headers = build_headers(
            referer="https://default.com/",
            cookie="old=cookie",
            user_agent="OldUA",
            custom_headers=["referer: https://override.com/", "COOKIE: new=cookie", "user-agent: NewUA"]
        )

        self.assertEqual(headers["referer"], "https://override.com/")
        self.assertEqual(headers["COOKIE"], "new=cookie")
        self.assertEqual(headers["user-agent"], "NewUA")
        self.assertNotIn("Referer", headers)
        self.assertNotIn("Cookie", headers)
        self.assertNotIn("User-Agent", headers)

    def test_header_with_multiple_colons_in_value(self):
        headers = build_headers(custom_headers=["X-Custom: val:1:2:3"])
        self.assertEqual(headers["X-Custom"], "val:1:2:3")


class TestHeadersForRedirect(unittest.TestCase):
    """The proxy follows redirects itself, so it must drop credentials the way curl does."""
    HEADERS = {"Referer": "https://site.example/", "Cookie": "cf_clearance=abc", "authorization": "Bearer t"}

    def test_credentials_stay_behind_when_the_host_changes(self):
        out = headers_for_redirect(self.HEADERS, "https://site.example/a.m3u8", "https://cdn.other.example/a.m3u8")
        self.assertEqual(out, {"Referer": "https://site.example/"})

    def test_credentials_follow_a_redirect_on_the_same_host(self):
        out = headers_for_redirect(self.HEADERS, "https://site.example/a.m3u8", "https://site.example/b.m3u8")
        self.assertEqual(out, self.HEADERS)


if __name__ == "__main__":
    unittest.main()
