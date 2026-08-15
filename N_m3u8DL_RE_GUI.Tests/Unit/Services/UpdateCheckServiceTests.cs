using System;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_WithNullOrEmptyArgs_ShouldReturnNoUpdate()
    {
        var service = new GitHubUpdateCheckService();

        var resultNullOwner = await service.CheckForUpdateAsync("", "repo", new Version(2, 1, 3));
        var resultNullRepo = await service.CheckForUpdateAsync("owner", "", new Version(2, 1, 3));
        var resultNullVer = await service.CheckForUpdateAsync("owner", "repo", null);

        Assert.False(resultNullOwner.HasUpdate);
        Assert.False(resultNullRepo.HasUpdate);
        Assert.False(resultNullVer.HasUpdate);
    }

    [Fact]
    public async Task CheckForUpdateAsync_WithNonExistentRepo_ShouldHandleGracefullyWithoutThrowing()
    {
        var service = new GitHubUpdateCheckService();

        var exception = await Record.ExceptionAsync(() => 
            service.CheckForUpdateAsync("nonexistent-user-123456789", "nonexistent-repo-987654321", new Version(2, 1, 3))
        );

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("2.1.3", "2.1.4", true)]
    [InlineData("2.1.3", "2.2.0", true)]
    [InlineData("2.1.3", "3.0.0", true)]
    [InlineData("2.1.3", "2.1.3", false)]
    [InlineData("2.1.3", "2.1.2", false)]
    public void VersionComparison_LogicVerification(string current, string latest, bool expectedHasUpdate)
    {
        var currentVer = Version.Parse(current);
        var latestVer = Version.Parse(latest);

        bool hasUpdate = latestVer > currentVer;

        Assert.Equal(expectedHasUpdate, hasUpdate);
    }

    [Fact]
    public async Task CheckForUpdateAsync_CalledConcurrently_ShouldNotThrow()
    {
        var service = new GitHubUpdateCheckService();
        var ver = new Version(2, 1, 3);
        
        var tasks = System.Linq.Enumerable.Range(0, 3).Select(_ =>
            Record.ExceptionAsync(() => service.CheckForUpdateAsync("nonexistent-user-123", "nonexistent-repo-456", ver))
        );
        var exceptions = await Task.WhenAll(tasks);
        Assert.All(exceptions, ex => Assert.Null(ex));
    }
}
