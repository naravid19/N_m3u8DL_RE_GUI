#nullable enable
using System.IO;
using System.Linq;
using System.Text;
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

/// <summary>
/// Boundary coverage for <see cref="TextEncodingDetector"/>: the 8 KB sampling window,
/// every BOM shape, non-seekable streams, and the ANSI fallback branch.
/// </summary>
public class TextEncodingDetectorEdgeTests
{
    private const int SampleSize = 8192;

    [Fact]
    public void AnsiFallbackAndUtf8_AreDistinctInstances_SoBranchAssertionsAreMeaningful()
    {
        Assert.NotSame(TextEncodingDetector.AnsiFallback, Encoding.UTF8);
    }

    [Fact]
    public void DetectFromStream_WithUtf16BigEndianBom_ShouldReturnBigEndianUnicode()
    {
        using var stream = new MemoryStream(new byte[] { 0xFE, 0xFF, 0x00, 0x41 });

        Assert.Same(Encoding.BigEndianUnicode, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WithSingleAsciiByte_ShouldReturnUtf8()
    {
        using var stream = new MemoryStream(new byte[] { 0x41 });

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WithTruncatedUtf8Bom_ShouldNotBeTreatedAsBom()
    {
        // 0xEF 0xBB alone is not a BOM, and is not a complete UTF-8 sequence either.
        using var stream = new MemoryStream(new byte[] { 0xEF, 0xBB });

        Assert.Same(TextEncodingDetector.AnsiFallback, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WithMultiByteUtf8WellInsideSample_ShouldReturnUtf8()
    {
        var payload = Encoding.UTF8.GetBytes("รายการ 中文 — em dash\nhttps://example.com/a.m3u8\n");
        using var stream = new MemoryStream(payload);

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WithLargeValidUtf8File_ShouldReturnUtf8()
    {
        var text = string.Concat(Enumerable.Repeat("https://example.com/aaaa.m3u8\n", 2000));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WhenSequenceEndsExactlyOnSampleBoundary_ShouldReturnUtf8()
    {
        var bytes = new byte[SampleSize + 16];
        Array.Fill(bytes, (byte)'a', 0, SampleSize - 2);
        // 'é' == 0xC3 0xA9, both bytes inside the window.
        bytes[SampleSize - 2] = 0xC3;
        bytes[SampleSize - 1] = 0xA9;
        Array.Fill(bytes, (byte)'b', SampleSize, 16);

        using var stream = new MemoryStream(bytes);

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WhenMultiByteSequenceStraddlesSampleBoundary_ShouldStillReturnUtf8()
    {
        // The sample is cut at exactly 8192 bytes. A sequence that starts inside the window
        // and finishes outside it is not evidence of non-UTF-8 data.
        var bytes = new byte[SampleSize + 16];
        Array.Fill(bytes, (byte)'a', 0, SampleSize - 1);
        bytes[SampleSize - 1] = 0xC3; // lead byte is the last byte of the sample
        bytes[SampleSize] = 0xA9;     // continuation byte is never read
        Array.Fill(bytes, (byte)'b', SampleSize + 1, 15);

        using var stream = new MemoryStream(bytes);

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WithLegacyAnsiHighBytes_ShouldReturnTheAnsiFallback()
    {
        // 0xB4 0xF2 is a valid GBK character but invalid UTF-8.
        using var stream = new MemoryStream(new byte[] { (byte)'a', 0xB4, 0xF2, (byte)'b' });

        Assert.Same(TextEncodingDetector.AnsiFallback, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void AnsiFallback_ShouldActuallyDecodeLegacyBytes()
    {
        // On .NET Framework Encoding.Default was the system ANSI code page. On .NET Core
        // it is UTF-8, so the old fallback could not recover a legacy GBK/Big5 batch list.
        var decoded = TextEncodingDetector.AnsiFallback.GetString(new byte[] { 0xB4, 0xF2 });

        Assert.NotEqual("utf-8", TextEncodingDetector.AnsiFallback.WebName);
        Assert.DoesNotContain('\uFFFD', decoded);
    }

    [Fact]
    public void DetectFromStream_WithNonSeekableStream_ShouldStillDetect()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("https://example.com/a.m3u8"));
        using var stream = new NonSeekableStream(inner);

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromStream_WithChunkedStream_ShouldReadFullSampleNotJustFirstChunk()
    {
        // ReadSample must loop. A stream handing back one byte per Read call still has to
        // fill the window, otherwise detection silently runs on a 1-byte sample.
        var payload = new byte[64];
        Array.Fill(payload, (byte)'a');
        payload[40] = 0xB4; // invalid UTF-8, only reachable once the whole sample is read
        payload[41] = 0x21;

        using var inner = new MemoryStream(payload);
        using var stream = new DripFeedStream(inner);

        Assert.Same(TextEncodingDetector.AnsiFallback, TextEncodingDetector.DetectFromStream(stream));
    }

    [Fact]
    public void DetectFromFile_ShouldOpenWithShareRead_SoConcurrentReadersAreNotBlocked()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "https://example.com/a.m3u8");

            using (new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var exception = Record.Exception(() => TextEncodingDetector.DetectFromFile(tempFile));
                Assert.Null(exception);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFromFile_WithMissingFile_ShouldThrowFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.txt");

        Assert.Throws<FileNotFoundException>(() => TextEncodingDetector.DetectFromFile(missing));
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableStream(Stream inner) => _inner = inner;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }

    /// <summary>Returns at most one byte per <see cref="Read"/> call.</summary>
    private sealed class DripFeedStream : Stream
    {
        private readonly Stream _inner;

        public DripFeedStream(Stream inner) => _inner = inner;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(1, count));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
