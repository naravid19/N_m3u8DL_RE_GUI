#nullable enable
using System.Runtime.InteropServices;

namespace N_m3u8DL_RE_GUI.Tests.Fixtures;

/// <summary>
/// Splits a command line with the real Win32 parser, so quoting tests assert against
/// what a launched process (N_m3u8DL-RE, python) actually receives rather than against
/// a hand-written expectation string.
/// </summary>
public static class Win32CommandLine
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LocalFree(nint hMem);

    /// <summary>
    /// Parses a command line with the exact same routine the CRT/Windows uses when a
    /// process reads its own arguments.
    /// </summary>
    public static string[] Parse(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var argc);
        if (argv == nint.Zero)
            throw new InvalidOperationException($"CommandLineToArgvW failed for: {commandLine}");

        try
        {
            var result = new string[argc];
            for (var i = 0; i < argc; i++)
            {
                var ptr = Marshal.ReadIntPtr(argv, i * nint.Size);
                result[i] = Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
