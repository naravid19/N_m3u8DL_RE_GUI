#nullable enable
using System;
using System.IO;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core.NativeBridge;

/// <summary>
/// Chrome's native messaging stdio framing: a 4-byte unsigned length in the
/// platform's native byte order, followed by that many bytes of UTF-8 JSON.
/// </summary>
public static class NativeMessageCodec
{
    /// <summary>
    /// Chrome refuses to send a host more than 1 MB in one message, so a larger
    /// declared length is a corrupt or hostile frame rather than a big payload,
    /// and is rejected before the buffer is allocated.
    /// </summary>
    public const int MaxMessageBytes = 1024 * 1024;

    /// <summary>
    /// Reads one message. Returns null when the stream ends cleanly at a frame
    /// boundary, which is how Chrome signals it is done with the host.
    /// </summary>
    public static string? ReadMessage(Stream input)
    {
        var lengthBytes = new byte[4];
        if (!TryReadExactly(input, lengthBytes))
            return null;

        // BitConverter follows the platform's byte order, which is what the
        // protocol asks for. Windows is little-endian, so this matches Chrome.
        var length = BitConverter.ToUInt32(lengthBytes, 0);
        if (length == 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Native message length out of range: {length}");

        var payload = new byte[length];
        if (!TryReadExactly(input, payload))
            throw new InvalidDataException("Native message body ended early.");

        return Encoding.UTF8.GetString(payload);
    }

    public static void WriteMessage(Stream output, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxMessageBytes)
            throw new InvalidDataException($"Native message too large: {payload.Length} bytes");

        output.Write(BitConverter.GetBytes((uint)payload.Length));
        output.Write(payload);
        output.Flush();
    }

    /// <summary>
    /// Fills the whole buffer, or reports that the stream ended. A pipe hands
    /// back short reads, so a single Read is not enough.
    /// </summary>
    private static bool TryReadExactly(Stream input, Span<byte> buffer)
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = input.Read(buffer[filled..]);
            if (read == 0)
                return false;
            filled += read;
        }
        return true;
    }
}
