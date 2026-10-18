import unittest
from unittest.mock import patch, MagicMock
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from m3u8_cf_bypass import probe_media_info, Logger


class TestProbeMediaInfo(unittest.TestCase):
    def test_nonexistent_file_returns_early(self):
        # Should not raise exception
        probe_media_info("non_existent_file_xyz_123.ts")

    @patch("os.path.exists", return_value=True)
    @patch("subprocess.run")
    def test_probe_parses_video_and_audio_streams(self, mock_run, mock_exists):
        fake_stderr = (
            "Input #0, mpegts, from 'seg0.ts':\n"
            "  Duration: 00:00:06.00, start: 1.400000, bitrate: 2134 kb/s\n"
            "  Stream #0:0[0x100]: Video: h264 (High) ([27][0][0][0] / 0x001B), yuv420p(tv, bt709), 1920x1080 [SAR 1:1 DAR 16:9], 25 fps, 25 tbr, 90k tbn\n"
            "  Stream #0:1[0x101]: Audio: aac (LC) ([15][0][0][0] / 0x000F), 48000 Hz, stereo, fltp, 128 kb/s\n"
        )
        mock_run.return_value = MagicMock(stderr=fake_stderr, stdout="", returncode=0)

        logs = []
        with patch.object(Logger, "info", side_effect=lambda msg: logs.append(msg)):
            probe_media_info("seg0.ts", ffmpeg_cmd="ffmpeg")

        mock_run.assert_called_once()
        self.assertTrue(any("Video" in log and "1920x1080" in log for log in logs))
        self.assertTrue(any("Audio" in log for log in logs))

    @patch("os.path.exists", return_value=True)
    @patch("subprocess.run", side_effect=FileNotFoundError("ffmpeg not found"))
    def test_probe_handles_missing_ffmpeg(self, mock_run, mock_exists):
        # Should not crash when ffmpeg is missing
        probe_media_info("seg0.ts", ffmpeg_cmd="invalid_ffmpeg_cmd")


if __name__ == "__main__":
    unittest.main()
