#nullable enable
using System;
using System.IO;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Provides encoding detection for text files used by batch input mode.
/// </summary>
public static class TextEncodingDetector
{
    public static Encoding DetectFromFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return DetectFromStream(stream);
    }

    public static Encoding DetectFromStream(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
            {
                if (stream.Length == 0)
                    return Encoding.UTF8;

                // Prevent overflow and excessive memory allocations for unexpected stream sizes.
                if (stream.Length > int.MaxValue)
                    return Encoding.Default;

                stream.Position = 0;
            }

            using var reader = new BinaryReader(stream, Encoding.Default, leaveOpen: true);
            var bytes = stream.CanSeek
                ? reader.ReadBytes((int)stream.Length)
                : ReadAllBytes(reader);

            if (bytes.Length == 0)
                return Encoding.UTF8;

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8;

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode;

            return IsUtf8Bytes(bytes) ? Encoding.UTF8 : Encoding.Default;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static byte[] ReadAllBytes(BinaryReader reader)
    {
        using var buffer = new MemoryStream();
        const int chunkSize = 4096;
        while (true)
        {
            var chunk = reader.ReadBytes(chunkSize);
            if (chunk.Length == 0)
                break;

            buffer.Write(chunk, 0, chunk.Length);
        }

        return buffer.ToArray();
    }

    private static bool IsUtf8Bytes(byte[] data)
    {
        var charByteCounter = 1;
        byte currentByte;

        for (var i = 0; i < data.Length; i++)
        {
            currentByte = data[i];
            if (charByteCounter == 1)
            {
                if (currentByte >= 0x80)
                {
                    while (((currentByte <<= 1) & 0x80) != 0)
                    {
                        charByteCounter++;
                    }

                    if (charByteCounter == 1 || charByteCounter > 6)
                        return false;
                }
            }
            else
            {
                if ((currentByte & 0xC0) != 0x80)
                    return false;

                charByteCounter--;
            }
        }

        return charByteCounter <= 1;
    }
}
