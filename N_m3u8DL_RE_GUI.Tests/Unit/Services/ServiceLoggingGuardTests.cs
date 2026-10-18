#nullable enable
using System.IO;
using System.Linq;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class ServiceLoggingGuardTests
{
    [Fact]
    public void FailuresAreLoggedThroughTraceSoReleaseBuildsKeepTheMessage()
    {
        // Debug.WriteLine is compiled out of Release builds, so a failure logged that way
        // leaves nothing behind in the build users actually run -- including the global
        // handlers that are all a crash report would have.
        var root = ProjectSource.SolutionRoot();
        var offenders = new[] { "N_m3u8DL_RE_GUI", "N_m3u8DL_RE_GUI.Core" }
            .SelectMany(project => Directory.GetFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => File.ReadAllText(file).Contains("Debug.WriteLine("))
            .Select(Path.GetFileName)
            .OrderBy(name => name)
            .ToList();

        Assert.Empty(offenders);
    }
}
