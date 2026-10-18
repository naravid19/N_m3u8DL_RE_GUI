#nullable enable
using System;
using System.IO;

namespace N_m3u8DL_RE_GUI.Tests.Fixtures;

/// <summary>
/// Reads the GUI project's own source, for guard tests on code that needs a live WPF
/// window and an STA pump and so cannot be driven from a unit test. Weaker than running
/// the code, but it fails loudly when a guarded call is removed.
/// </summary>
public static class ProjectSource
{
    /// <summary>The directory holding N_m3u8DL_RE_GUI.sln.</summary>
    public static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI.sln")))
            dir = dir.Parent;

        // Failing loudly beats passing while reading nothing.
        return dir?.FullName ?? throw new DirectoryNotFoundException($"Could not locate the solution root from {AppContext.BaseDirectory}");
    }

    /// <summary>Reads a file of the GUI project, by its path relative to N_m3u8DL_RE_GUI/.</summary>
    public static string ReadFile(string relativePath) =>
        File.ReadAllText(Path.Combine(SolutionRoot(), "N_m3u8DL_RE_GUI", relativePath));

    /// <summary>The body of the method declared by <paramref name="signature"/>, brace-matched.</summary>
    public static string MethodBody(string relativePath, string signature)
    {
        var source = ReadFile(relativePath);
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"'{signature}' not found in {relativePath} -- was it renamed?");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i >= 0 && i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Unbalanced braces after '{signature}' in {relativePath}");
    }
}
