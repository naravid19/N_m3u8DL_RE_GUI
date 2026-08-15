#nullable enable
using System.IO;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class UtilityServiceTests
{
    [Fact]
    public async Task GetTitleFromUrlAsync_WithNonHttpInput_ShouldReturnEmpty()
    {
        using var service = new UtilityService();
        var localPathResult = await service.GetTitleFromUrlAsync(@"C:\videos\sample.m3u8");
        var plainTextResult = await service.GetTitleFromUrlAsync("not-a-url");
        var emptyResult = await service.GetTitleFromUrlAsync("");

        Assert.Equal(string.Empty, localPathResult);
        Assert.Equal(string.Empty, plainTextResult);
        Assert.Equal(string.Empty, emptyResult);
    }

    [Theory]
    [InlineData("video<1>title*.mp4", "video_1_title_.mp4")]
    [InlineData(@"illegal/name\with|pipes?", "illegal_name_with_pipes_")]
    [InlineData("  valid_filename.m3u8  ", "valid_filename.m3u8")]
    public void GetValidFileName_ShouldReplaceInvalidPathChars(string input, string expected)
    {
        using var service = new UtilityService();
        var result = service.GetValidFileName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CON.mp4", "_CON.mp4")]
    [InlineData("prn.m3u8", "_prn.m3u8")]
    [InlineData("AUX", "_AUX")]
    [InlineData("NUL.txt", "_NUL.txt")]
    [InlineData("COM1.ts", "_COM1.ts")]
    [InlineData("LPT9.mkv", "_LPT9.mkv")]
    public void GetValidFileName_ShouldSanitizeReservedDosDeviceNames(string input, string expected)
    {
        using var service = new UtilityService();
        var result = service.GetValidFileName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ตอนที่ 1 ดาวน์โหลด", "ตอนที่ 1 ดาวน์โหลด")]
    [InlineData("第01集_测试视频", "第01集_测试视频")]
    [InlineData("エピソード1_アニメ", "エピソード1_アニメ")]
    [InlineData("Café_René_1080p.mp4", "Café_René_1080p.mp4")]
    public void GetValidFileName_ShouldPreserveUnicodeCharacters(string input, string expected)
    {
        using var service = new UtilityService();
        var result = service.GetValidFileName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetValidFileName_NullOrEmpty_ReturnsEmptyString(string? input)
    {
        using var service = new UtilityService();
        var result = service.GetValidFileName(input!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void FileExists_ExistingFile_ReturnsTrue()
    {
        using var service = new UtilityService();
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(service.FileExists(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FileExists_NonExistentFileOrNull_ReturnsFalse()
    {
        using var service = new UtilityService();
        Assert.False(service.FileExists(@"C:\non_existent_path_123456789.txt"));
        Assert.False(service.FileExists(null!));
        Assert.False(service.FileExists(""));
    }

    [Theory]
    [InlineData(@"C:\videos\playlist.m3u8", ".m3u8")]
    [InlineData("manifest.mpd", ".mpd")]
    [InlineData("noextension", "")]
    [InlineData(null, "")]
    public void GetFileExtension_ReturnsExpectedExtension(string? path, string expected)
    {
        using var service = new UtilityService();
        Assert.Equal(expected, service.GetFileExtension(path!));
    }

    [Theory]
    [InlineData("CON.txt.bak", "_CON.txt.bak")]
    [InlineData("con.mp4.part", "_con.mp4.part")]
    [InlineData("NUL.a.b.c", "_NUL.a.b.c")]
    [InlineData("COM1.log.1", "_COM1.log.1")]
    [InlineData("LPT9.x.y", "_LPT9.x.y")]
    public void GetValidFileName_ShouldSanitizeReservedNamesBeforeTheFirstDot(string input, string expected)
    {
        Assert.Equal(expected, new UtilityService().GetValidFileName(input));
    }

    [Theory]
    [InlineData("CONSOLE.txt")]
    [InlineData("CONTENT.mp4")]
    [InlineData("COM10.log")]
    [InlineData("MyCON.txt")]
    [InlineData("NULL.txt")]
    public void GetValidFileName_ShouldNotTouchNamesThatMerelyStartWithAReservedWord(string input)
    {
        Assert.Equal(input, new UtilityService().GetValidFileName(input));
    }
}
