using System;
using System.IO;
using System.Linq;
using N_m3u8DL_RE_GUI.Core.NativeBridge;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.NativeBridge;

public sealed class CaptureInboxTests : IDisposable
{
    private readonly string _dir;
    private readonly CaptureInbox _inbox;

    public CaptureInboxTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nre_inbox_" + Guid.NewGuid().ToString("N")[..8]);
        _inbox = new CaptureInbox(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void AnEmptyInboxHasNothingToTake()
    {
        Assert.False(_inbox.HasPending());
        Assert.Null(_inbox.TakeOldest());
    }

    [Fact]
    public void RoundTripsAPayload()
    {
        _inbox.Write("curl 'https://example.test/a.m3u8'");

        Assert.True(_inbox.HasPending());
        Assert.Equal("curl 'https://example.test/a.m3u8'", _inbox.TakeOldest());
    }

    [Fact]
    public void TakingRemovesTheEntry()
    {
        _inbox.Write("one");
        _inbox.TakeOldest();

        Assert.False(_inbox.HasPending());
        Assert.Null(_inbox.TakeOldest());
    }

    [Fact]
    public void DrainsOldestFirst()
    {
        // This ordering is the queue: several Download clicks while a download
        // is running must come back out in the order they were clicked.
        _inbox.Write("first");
        _inbox.Write("second");
        _inbox.Write("third");

        Assert.Equal("first", _inbox.TakeOldest());
        Assert.Equal("second", _inbox.TakeOldest());
        Assert.Equal("third", _inbox.TakeOldest());
        Assert.Null(_inbox.TakeOldest());
    }

    [Fact]
    public void PreservesAMultiLinePayloadExactly()
    {
        const string payload = "curl 'https://x/y.m3u8' \\\n  -H 'Cookie: a=1|2|3'\n# nre-save-name: ตอน 84";
        _inbox.Write(payload);

        Assert.Equal(payload, _inbox.TakeOldest());
    }

    [Fact]
    public void NeverLeavesAPartialFileVisible()
    {
        // Writes land on a .tmp name and are moved into place, so a reader
        // scanning mid-write cannot pick up a half-written payload.
        _inbox.Write("payload");

        var visible = Directory.GetFiles(_dir, "*.json");
        Assert.Single(visible);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void IgnoresUnrelatedFilesInTheDirectory()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "not a capture");

        Assert.False(_inbox.HasPending());
    }

    [Fact]
    public void SkipsAndRemovesAnUnreadableEntry()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "0000000000000000001-deadbeef.json"), "{ not json");
        _inbox.Write("good");

        Assert.Equal("good", _inbox.TakeOldest());
        Assert.Empty(Directory.GetFiles(_dir, "*.json"));
    }

    [Fact]
    public void DefaultsToLocalAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N_m3u8DL_RE_GUI",
            "inbox");

        Assert.Equal(expected, new CaptureInbox().Directory);
    }
}
