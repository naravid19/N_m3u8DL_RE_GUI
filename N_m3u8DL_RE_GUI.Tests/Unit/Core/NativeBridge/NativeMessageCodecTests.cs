using System;
using System.IO;
using System.Text;
using N_m3u8DL_RE_GUI.Core.NativeBridge;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.NativeBridge;

public class NativeMessageCodecTests
{
    [Fact]
    public void RoundTripsAMessage()
    {
        using var buffer = new MemoryStream();
        NativeMessageCodec.WriteMessage(buffer, "{\"ok\":true}");
        buffer.Position = 0;

        Assert.Equal("{\"ok\":true}", NativeMessageCodec.ReadMessage(buffer));
    }

    [Fact]
    public void WritesAFourByteLittleEndianLengthPrefix()
    {
        using var buffer = new MemoryStream();
        NativeMessageCodec.WriteMessage(buffer, "ab");

        var bytes = buffer.ToArray();
        Assert.Equal(6, bytes.Length);
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, bytes[..4]);
    }

    [Fact]
    public void RoundTripsNonAsciiAsUtf8()
    {
        // A save-name directive routinely carries non-ASCII; the length prefix
        // counts bytes, not characters, and getting that wrong truncates.
        const string message = "{\"payload\":\"ตอนที่ 84 — 日本語\"}";
        using var buffer = new MemoryStream();
        NativeMessageCodec.WriteMessage(buffer, message);
        buffer.Position = 0;

        Assert.Equal(message, NativeMessageCodec.ReadMessage(buffer));
    }

    [Fact]
    public void ReturnsNullOnCleanEof()
    {
        using var empty = new MemoryStream();
        Assert.Null(NativeMessageCodec.ReadMessage(empty));
    }

    [Fact]
    public void ThrowsOnATruncatedBody()
    {
        using var buffer = new MemoryStream();
        buffer.Write(BitConverter.GetBytes(10u));
        buffer.Write(Encoding.UTF8.GetBytes("abc"));
        buffer.Position = 0;

        Assert.Throws<InvalidDataException>(() => NativeMessageCodec.ReadMessage(buffer));
    }

    [Fact]
    public void ThrowsOnAnOversizeFrameWithoutAllocatingIt()
    {
        using var buffer = new MemoryStream();
        buffer.Write(BitConverter.GetBytes((uint)(NativeMessageCodec.MaxMessageBytes + 1)));
        buffer.Position = 0;

        Assert.Throws<InvalidDataException>(() => NativeMessageCodec.ReadMessage(buffer));
    }

    [Fact]
    public void RejectsAnOversizeOutgoingMessage()
    {
        using var buffer = new MemoryStream();
        var huge = new string('x', NativeMessageCodec.MaxMessageBytes + 1);

        Assert.Throws<InvalidDataException>(() => NativeMessageCodec.WriteMessage(buffer, huge));
    }
}
