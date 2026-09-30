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
    private const int MaxSampleSize = 8192; // 8KB sample is sufficient for BOM & UTF-8 check

    /// <summary>
    /// The system ANSI code page, or UTF-8 where it is unavailable. Encoding.Default is
    /// UTF-8 on .NET Core, which made the non-UTF-8 branch a no-op.
    /// </summary>
    public static Encoding AnsiFallback { get; } = ResolveAnsiFallback();

    private static Encoding ResolveAnsiFallback()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(0);   // 0 = the process's ANSI code page
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }

    public static Encoding DetectFromFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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

                stream.Position = 0;
            }

            var buffer = new byte[MaxSampleSize];
            var bytesRead = ReadSample(stream, buffer);

            if (bytesRead == 0)
                return Encoding.UTF8;

            if (bytesRead >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return Encoding.UTF8;

            if (bytesRead >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            if (bytesRead >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
                return Encoding.Unicode;

            return IsUtf8Bytes(buffer, bytesRead) ? Encoding.UTF8 : AnsiFallback;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static int ReadSample(Stream stream, byte[] buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }
        return totalRead;
    }

    /// <summary>
    /// Validates UTF-8 structure over a sample. A multi-byte sequence that begins inside
    /// the sample but finishes past its end is accepted: the sample boundary is arbitrary
    /// and truncation there says nothing about the file's encoding.
    /// </summary>
    private static bool IsUtf8Bytes(byte[] data, int length)
    {
        var charByteCounter = 1;

        for (var i = 0; i < length; i++)
        {
            byte currentByte = data[i];
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

        // charByteCounter > 1 means the last sequence is incomplete. That is only a real
        // failure when we reached the true end of the data, not the end of the sample.
        return charByteCounter <= 1 || length == MaxSampleSize;
    }
}
