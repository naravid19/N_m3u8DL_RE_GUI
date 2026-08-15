#nullable enable
using System;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Escaping for the legacy "key=value;key=value" config.txt format.
///
/// Only two characters need encoding: ';' because it is the record separator, and '%'
/// because it introduces an escape. '=' is safe — the reader splits on the first one only.
/// Values written before this codec existed contain no '%' sequences and therefore decode
/// to themselves, which is what keeps old files loading.
/// </summary>
public static class LegacyConfigCodec
{
    /// <summary>Cached, for the same reason ArgsBuilder caches its escape set.</summary>
    private static readonly char[] NeedsEscaping = { ';', '%' };

    public static string EscapeValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.IndexOfAny(NeedsEscaping) < 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '%': sb.Append("%25"); break;
                case ';': sb.Append("%3B"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    public static string UnescapeValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.IndexOf('%') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && TryHex(value[i + 1], value[i + 2], out var decoded))
            {
                sb.Append(decoded);
                i += 2;
                continue;
            }
            sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static bool TryHex(char high, char low, out char result)
    {
        result = '\0';
        if (!Uri.IsHexDigit(high) || !Uri.IsHexDigit(low))
            return false;

        var code = Convert.ToInt32($"{high}{low}", 16);
        // Only the two characters this codec produces are decoded. Anything else is a
        // literal '%' the user typed, and must survive untouched.
        if (code != '%' && code != ';')
            return false;

        result = (char)code;
        return true;
    }
}
