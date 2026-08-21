using System;
using System.Linq;
using System.Text;
using N_m3u8DL_RE_GUI.Core.Abyss;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit
{
    public class AbyssCryptoTests
    {
        [Fact]
        public void DeriveKey_WithStringInput_MatchesExpectedMd5Hex()
        {
            // Real sample from live HAR
            string mediaKey = "7206:EivD8IFMyk:29438996";
            string keyHex = AbyssCrypto.DeriveKey(mediaKey);

            Assert.Equal("857edaedfb0189e3fc1cce949ffb5de2", keyHex);
        }

        [Fact]
        public void DeriveKey_WithNumericInput_MatchesExpectedByteMappedMd5Hex()
        {
            // For size 393459318: digits [3,9,3,4,5,9,3,1,8] -> MD5 is "5b59663532d090af42dc628d83a660f7"
            long size = 393459318;
            string keyHex = AbyssCrypto.DeriveKey(size);

            Assert.Equal("5b59663532d090af42dc628d83a660f7", keyHex);
        }

        [Fact]
        public void AesCtrTransform_IsSymmetricAndRoundTrips()
        {
            byte[] key = Encoding.UTF8.GetBytes("857edaedfb0189e3fc1cce949ffb5de2");
            byte[] iv = new byte[16];
            Array.Copy(key, 0, iv, 0, 16);

            byte[] original = Encoding.UTF8.GetBytes("{\"test\":\"abyss_stream_payload_12345\"}");

            byte[] encrypted = AbyssCrypto.AesCtrTransform(original, key, iv);
            Assert.NotEqual(original, encrypted);

            byte[] decrypted = AbyssCrypto.AesCtrTransform(encrypted, key, iv);
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void DoubleBase64_EncodesWithoutPaddingCharacters()
        {
            byte[] testBytes = Encoding.UTF8.GetBytes("/mp4/29438996/5/393459318/2097152/0");
            string token = AbyssCrypto.DoubleBase64(testBytes);

            Assert.DoesNotContain("=", token);
            Assert.True(token.Length > 0);
        }

        [Fact]
        public void AbyssDownloadProgress_CalculatesCorrectPercentageSpeedAndETA()
        {
            var progress = new AbyssDownloadProgress
            {
                DownloadedChunks = 50,
                TotalChunks = 100,
                DownloadedBytes = 50 * 1024 * 1024,
                TotalBytes = 100 * 1024 * 1024,
                SpeedBytesPerSec = 10 * 1024 * 1024,
                Eta = TimeSpan.FromSeconds(5)
            };

            Assert.Equal(50.0, progress.Percentage);
            string statusStr = progress.ToString();
            Assert.Contains("10.00 MB/s", statusStr);
            Assert.Contains("50.0 MB / 100.0 MB", statusStr);
            Assert.Contains("50.0%", statusStr);
            Assert.Contains("Seg: 50/100", statusStr);
            Assert.Contains("ETA: 00:00:05", statusStr);

            string reLogLine = progress.FormatN_m3u8DL_RE_Line();
            Assert.Contains("INFO : 10.00MB/s 50.0MB/100.0MB 50.0% 00:00:05 [50/100]", reLogLine);
        }
    }
}
