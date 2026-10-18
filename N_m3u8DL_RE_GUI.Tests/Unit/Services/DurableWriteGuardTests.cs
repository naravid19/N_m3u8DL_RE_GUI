using System;
using System.IO;
using System.Linq;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class DurableWriteGuardTests
{
    /// <summary>
    /// Files whose contents the user cannot recreate: settings carrying DPAPI
    /// ciphertext, and the record that makes an interrupted download
    /// resumable. Temp scripts and self-healing manifests are deliberately
    /// absent -- they are rebuilt on demand.
    /// </summary>
    public static TheoryData<string> StatefulWriters => new()
    {
        "Services/JsonConfigService.cs",
        "Services/ResumeJobStore.cs",
        "Services/ConfigService.cs"
    };

    [Theory]
    [MemberData(nameof(StatefulWriters))]
    public void UserStateIsWrittenAtomically(string relativePath)
    {
        var source = ReadProjectFile(relativePath);

        Assert.DoesNotMatch(@"(?<!Atomic)File\.WriteAllText", source);
        Assert.Contains("AtomicFile.WriteAllText", source);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        // Failing loudly beats passing while reading nothing.
        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
