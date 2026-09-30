#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class OptionValueNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void NormalizeSaveDir_NullOrEmpty_ReturnsNull(string? input, string? expected)
    {
        Assert.Equal(expected, OptionValueNormalizer.NormalizeSaveDir(input));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"c:\", @"C:\")]
    [InlineData(@"D:/", @"D:\")]
    [InlineData(@"z:\", @"Z:\")]
    public void NormalizeSaveDir_DriveRoot_PreservesDriveFormat(string input, string expected)
    {
        Assert.Equal(expected, OptionValueNormalizer.NormalizeSaveDir(input));
    }

    [Theory]
    [InlineData(@"C:\Downloads\", @"C:\Downloads")]
    [InlineData(@"C:\Downloads/folder/", @"C:\Downloads/folder")]
    [InlineData(@"D:\Media\SubFolder\", @"D:\Media\SubFolder")]
    public void NormalizeSaveDir_NormalPath_TrimsTrailingSeparators(string input, string expected)
    {
        Assert.Equal(expected, OptionValueNormalizer.NormalizeSaveDir(input));
    }

    [Theory]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"\\192.168.1.100\videos\", @"\\192.168.1.100\videos")]
    public void NormalizeSaveDir_UncPath_TrimsTrailingBackslashes(string input, string expected)
    {
        Assert.Equal(expected, OptionValueNormalizer.NormalizeSaveDir(input));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("\\", "\\")]
    public void NormalizeSaveDir_SingleSlashRoots_Preserved(string input, string expected)
    {
        Assert.Equal(expected, OptionValueNormalizer.NormalizeSaveDir(input));
    }
}
