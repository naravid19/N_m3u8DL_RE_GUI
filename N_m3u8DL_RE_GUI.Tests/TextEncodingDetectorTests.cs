#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System.Text;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class TextEncodingDetectorTests
{
    [Fact]
    public void DetectFromFile_WithEmptyFile_ShouldReturnUtf8()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, Array.Empty<byte>());

            var encoding = TextEncodingDetector.DetectFromFile(tempFile);

            Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFromFile_WithUtf8Bom_ShouldReturnUtf8()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0xEF, 0xBB, 0xBF, 0x41 });

            var encoding = TextEncodingDetector.DetectFromFile(tempFile);

            Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFromFile_WithUtf16LeBom_ShouldReturnUnicode()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0xFF, 0xFE, 0x41, 0x00 });

            var encoding = TextEncodingDetector.DetectFromFile(tempFile);

            Assert.Equal(Encoding.Unicode.WebName, encoding.WebName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFromStream_WithIncompleteUtf8Sequence_ShouldReturnDefaultEncoding()
    {
        using var stream = new MemoryStream(new byte[] { 0xE2, 0x82 });

        var encoding = TextEncodingDetector.DetectFromStream(stream);

        Assert.Equal(Encoding.Default.WebName, encoding.WebName);
    }

    [Fact]
    public void DetectFromStream_WithAsciiData_ShouldReturnUtf8()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("https://example.com/test.m3u8"));

        var encoding = TextEncodingDetector.DetectFromStream(stream);

        Assert.Equal(Encoding.UTF8.WebName, encoding.WebName);
    }

    [Fact]
    public void DetectFromStream_ShouldPreserveOriginalPosition()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        stream.Position = 2;

        _ = TextEncodingDetector.DetectFromStream(stream);

        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void DetectFromStream_WithVeryLargeLength_ShouldReturnDefaultEncoding()
    {
        using var stream = new LargeLengthTestStream(new byte[] { 0x41 });

        var encoding = TextEncodingDetector.DetectFromStream(stream);

        Assert.Equal(Encoding.Default.WebName, encoding.WebName);
    }

    private sealed class LargeLengthTestStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public LargeLengthTestStream(byte[] data)
        {
            _data = data;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => (long)int.MaxValue + 1;

        public override long Position
        {
            get => _position;
            set => _position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length)
                return 0;

            var toCopy = Math.Min(count, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return origin switch
            {
                SeekOrigin.Begin => Position = offset,
                SeekOrigin.Current => Position += offset,
                SeekOrigin.End => Position = _data.Length + offset,
                _ => Position
            };
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush()
        {
        }
    }
}
