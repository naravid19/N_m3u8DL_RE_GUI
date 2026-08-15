#nullable enable
using System;
using System.Windows;
using N_m3u8DL_RE_GUI.Services;
using NSubstitute;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class DragDropServiceTests
{
    private readonly DragDropService _service = new();

    [Fact]
    public void HandleFileDrop_WithFileDropFormat_ShouldReturnFirstFile()
    {
        var dataObject = Substitute.For<IDataObject>();
        var files = new[] { @"C:\videos\test.m3u8", @"C:\videos\extra.m3u8" };

        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(true);
        dataObject.GetData(DataFormats.FileDrop).Returns(files);

        var result = _service.HandleFileDrop(dataObject);

        Assert.Equal(@"C:\videos\test.m3u8", result);
    }

    [Fact]
    public void HandleFileDrop_WithEmptyFileDropArray_ShouldFallbackOrReturnNull()
    {
        var dataObject = Substitute.For<IDataObject>();
        var emptyFiles = Array.Empty<string>();

        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(true);
        dataObject.GetData(DataFormats.FileDrop).Returns(emptyFiles);

        var result = _service.HandleFileDrop(dataObject);

        Assert.Null(result);
    }

    [Fact]
    public void HandleFileDrop_WithTextFormat_ShouldReturnTrimmedText()
    {
        var dataObject = Substitute.For<IDataObject>();
        const string rawText = "  https://example.com/playlist.m3u8  ";

        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(false);
        dataObject.GetDataPresent(DataFormats.Text).Returns(true);
        dataObject.GetData(DataFormats.Text).Returns(rawText);

        var result = _service.HandleFileDrop(dataObject);

        Assert.Equal("https://example.com/playlist.m3u8", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void HandleFileDrop_WithEmptyOrWhitespaceText_ShouldReturnNull(string? emptyText)
    {
        var dataObject = Substitute.For<IDataObject>();

        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(false);
        dataObject.GetDataPresent(DataFormats.Text).Returns(true);
        dataObject.GetData(DataFormats.Text).Returns(emptyText);

        var result = _service.HandleFileDrop(dataObject);

        Assert.Null(result);
    }

    [Fact]
    public void HandleFileDrop_WithNonDataObject_ShouldReturnNull()
    {
        var result = _service.HandleFileDrop("plain-string-object");
        Assert.Null(result);

        var nullResult = _service.HandleFileDrop(null!);
        Assert.Null(nullResult);
    }

    [Fact]
    public void HasFiles_WithFileDropPresent_ShouldReturnTrue()
    {
        var dataObject = Substitute.For<IDataObject>();
        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(true);

        Assert.True(_service.HasFiles(dataObject));
    }

    [Fact]
    public void HasFiles_WithTextPresent_ShouldReturnTrue()
    {
        var dataObject = Substitute.For<IDataObject>();
        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(false);
        dataObject.GetDataPresent(DataFormats.Text).Returns(true);

        Assert.True(_service.HasFiles(dataObject));
    }

    [Fact]
    public void HasFiles_WithNoSupportedFormat_ShouldReturnFalse()
    {
        var dataObject = Substitute.For<IDataObject>();
        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(false);
        dataObject.GetDataPresent(DataFormats.Text).Returns(false);

        Assert.False(_service.HasFiles(dataObject));
    }

    [Fact]
    public void HasFiles_WithNonDataObject_ShouldReturnFalse()
    {
        Assert.False(_service.HasFiles(new object()));
        Assert.False(_service.HasFiles(null!));
    }

    [Fact]
    public void GetFilePaths_WithFileDropFormat_ShouldReturnAllPaths()
    {
        var dataObject = Substitute.For<IDataObject>();
        var files = new[] { @"C:\videos\1.m3u8", @"C:\videos\2.mpd", @"C:\videos\3.txt" };

        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(true);
        dataObject.GetData(DataFormats.FileDrop).Returns(files);

        var result = _service.GetFilePaths(dataObject);

        Assert.Equal(3, result.Length);
        Assert.Equal(files, result);
    }

    [Fact]
    public void GetFilePaths_WithTextFormat_ShouldReturnSingleArrayElement()
    {
        var dataObject = Substitute.For<IDataObject>();
        const string text = "  https://test.com/stream.m3u8  ";

        dataObject.GetDataPresent(DataFormats.FileDrop).Returns(false);
        dataObject.GetDataPresent(DataFormats.Text).Returns(true);
        dataObject.GetData(DataFormats.Text).Returns(text);

        var result = _service.GetFilePaths(dataObject);

        Assert.Single(result);
        Assert.Equal("https://test.com/stream.m3u8", result[0]);
    }

    [Fact]
    public void GetFilePaths_WithUnsupportedOrNull_ShouldReturnEmptyArray()
    {
        Assert.Empty(_service.GetFilePaths(new object()));
        Assert.Empty(_service.GetFilePaths(null!));
    }
}
