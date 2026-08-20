#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Parses a "Copy as cURL" command from browser devtools into a CapturedRequest.
///
/// Handles the three dialects shipping browsers emit: bash (single quotes,
/// backslash continuation), cmd (double quotes, caret continuation and caret
/// escapes), and Firefox (double quotes, no continuation).
///
/// ponytail: heuristic tokenizer aimed at generated commands, not a POSIX shell
/// parser. It does not evaluate variables, subshells, or redirection — a
/// hand-written command using those will parse oddly. Upgrade path if that ever
/// matters: a real shell-word splitter.
/// </summary>
public static class CurlCommandParser
{
    /// <summary>Flags that consume the following token, so it is never the URL.</summary>
    private static readonly HashSet<string> ValueTakingFlags = new(StringComparer.Ordinal)
    {
        "-H", "--header", "-b", "--cookie", "-X", "--request",
        "-d", "--data", "--data-raw", "--data-binary", "--data-urlencode",
        "-A", "--user-agent", "-e", "--referer", "-u", "--user",
        "--url", "-o", "--output", "--connect-timeout", "--max-time", "-m",
    };

    public static bool LooksLikeCurl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("curl", StringComparison.OrdinalIgnoreCase))
            return false;

        // "curling is a sport" must not match; require a delimiter after the verb.
        return trimmed.Length == 4 || char.IsWhiteSpace(trimmed[4]);
    }

    public static CapturedRequest? Parse(string? text)
    {
        if (!LooksLikeCurl(text))
            return null;

        var tokens = Tokenize(text!);
        string? url = null;
        var headers = new List<CapturedHeader>();
        string? cookieFlagValue = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Equals("curl", StringComparison.OrdinalIgnoreCase) && url is null && i == 0)
                continue;

            if (token is "-H" or "--header")
            {
                if (i + 1 < tokens.Count)
                    AddHeader(headers, tokens[++i]);
                continue;
            }

            if (token is "-b" or "--cookie")
            {
                if (i + 1 < tokens.Count)
                    cookieFlagValue = tokens[++i];
                continue;
            }

            if (token is "-A" or "--user-agent")
            {
                if (i + 1 < tokens.Count)
                    AddHeader(headers, $"User-Agent: {tokens[++i]}");
                continue;
            }

            if (token is "-e" or "--referer")
            {
                if (i + 1 < tokens.Count)
                    AddHeader(headers, $"Referer: {tokens[++i]}");
                continue;
            }

            if (token is "--url")
            {
                if (i + 1 < tokens.Count && IsHttpUrl(tokens[i + 1]))
                    url = tokens[++i];
                continue;
            }

            if (ValueTakingFlags.Contains(token))
            {
                i++; // swallow the value so it is never mistaken for the URL
                continue;
            }

            if (token.StartsWith('-'))
                continue;

            url ??= IsHttpUrl(token) ? token : null;
        }

        if (url is null)
            return null;

        // -b only applies when no explicit Cookie header was given.
        if (cookieFlagValue is not null &&
            !headers.Any(h => h.Name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
        {
            AddHeader(headers, $"Cookie: {cookieFlagValue}");
        }

        return new CapturedRequest(url, headers, ClassifyUrl(url));
    }

    private static void AddHeader(List<CapturedHeader> headers, string raw)
    {
        var separator = raw.IndexOf(':');
        if (separator <= 0)
            return; // no colon, or a leading colon (pseudo-header) — nothing usable

        var name = raw[..separator].Trim();
        var value = raw[(separator + 1)..].Trim();

        if (!HeaderPolicy.ShouldForward(name) || value.Length == 0)
            return;

        headers.Add(new CapturedHeader(name, value));
    }

    private static bool IsHttpUrl(string token) =>
        Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Classifies by URL path only. Query strings routinely carry tokens
    /// ending in ".mp4" and would produce false positives.</summary>
    internal static CapturedStreamKind ClassifyUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return CapturedStreamKind.Unknown;

        var path = uri.AbsolutePath;

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Hls;

        if (path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Dash;

        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Media;

        return CapturedStreamKind.Unknown;
    }

    /// <summary>
    /// Splits a generated shell command into arguments. Adjacent quoted runs join
    /// into one token, which is what makes the bash 'a'\''b' idiom work.
    /// </summary>
    internal static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var hasToken = false;
        var i = 0;

        while (i < input.Length)
        {
            var c = input[i];

            // Line continuation: backslash (bash) or caret (cmd) before a newline.
            if ((c == '\\' || c == '^') && i + 1 < input.Length &&
                (input[i + 1] == '\n' || input[i + 1] == '\r'))
            {
                i++;
                while (i < input.Length && (input[i] == '\r' || input[i] == '\n'))
                    i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                i++;
                continue;
            }

            if (c == '\'')
            {
                hasToken = true;
                i++;
                while (i < input.Length && input[i] != '\'')
                    current.Append(input[i++]);
                i++; // closing quote
                continue;
            }

            if (c == '"')
            {
                hasToken = true;
                i++;
                while (i < input.Length && input[i] != '"')
                {
                    // cmd's escape char: drop it and re-examine what follows.
                    if (input[i] == '^' && i + 1 < input.Length)
                    {
                        i++;
                        continue;
                    }
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        current.Append(input[i + 1]);
                        i += 2;
                        continue;
                    }
                    current.Append(input[i++]);
                }
                i++; // closing quote
                continue;
            }

            if ((c == '\\' || c == '^') && i + 1 < input.Length)
            {
                current.Append(input[i + 1]);
                i += 2;
                hasToken = true;
                continue;
            }

            current.Append(c);
            hasToken = true;
            i++;
        }

        if (hasToken)
            tokens.Add(current.ToString());

        return tokens;
    }
}
