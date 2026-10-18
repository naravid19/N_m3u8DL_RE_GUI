#nullable enable
using System.Text;

namespace N_m3u8DL_RE_GUI.Tests.Fixtures;

/// <summary>
/// A verbatim port of N_m3u8DL-RE's own <c>ComplexParamParser.GetValue</c>
/// (references/upstream/N_m3u8DL-RE/src/N_m3u8DL-RE/CommandLine/ComplexParamParser.cs),
/// so tests can assert what the downloader actually reads out of a colon-delimited
/// option such as <c>--mux-after-done</c> rather than what we hoped it would read.
///
/// Do not "improve" this: its quirks are the contract we build against. Notably
/// <c>result.Replace("\\", "")</c> wipes every backslash accumulated so far each time
/// an escaped colon is met, so only the first escaped colon in a value survives intact.
/// </summary>
public static class ReComplexParam
{
    public static string? GetValue(string arg, string key)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(arg)) return null;

        var index = arg.IndexOf(key + "=", StringComparison.Ordinal);
        if (index == -1) return (arg.Contains(key) && arg.EndsWith(key)) ? "true" : null;

        var chars = arg[(index + key.Length + 1)..].ToCharArray();
        var result = new StringBuilder();
        char last = '\0';
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == ':')
            {
                if (last == '\\')
                {
                    result.Replace("\\", "");
                    last = chars[i];
                    result.Append(chars[i]);
                }
                else break;
            }
            else
            {
                last = chars[i];
                result.Append(chars[i]);
            }
        }

        var resultStr = result.ToString().Trim();

        // Only a matched pair of surrounding quotes is stripped; inner quotes stay.
        if (resultStr.Length >= 2
            && (resultStr[0] == '"' || resultStr[0] == '\'')
            && resultStr[^1] == resultStr[0])
        {
            resultStr = resultStr[1..^1];
        }

        return resultStr;
    }
}
