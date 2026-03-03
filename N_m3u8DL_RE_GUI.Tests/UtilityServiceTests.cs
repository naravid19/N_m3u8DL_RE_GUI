#nullable enable
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class UtilityServiceTests
{
    [Fact]
    public async Task GetTitleFromUrlAsync_WithNonHttpInput_ShouldReturnEmpty()
    {
        var service = new UtilityService();
        try
        {
            var localPathResult = await service.GetTitleFromUrlAsync(@"C:\videos\sample.m3u8");
            var plainTextResult = await service.GetTitleFromUrlAsync("not-a-url");

            Assert.Equal(string.Empty, localPathResult);
            Assert.Equal(string.Empty, plainTextResult);
        }
        finally
        {
            service.Dispose();
        }
    }
}
