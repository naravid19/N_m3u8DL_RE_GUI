using System;
using System.IO;
using System.Linq;
using System.Text;
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nre_atomic_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    [Fact]
    public void WritesTheContent()
    {
        var target = PathFor("a.json");

        AtomicFile.WriteAllText(target, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(target));
    }

    [Fact]
    public void ReplacesAnExistingFile()
    {
        var target = PathFor("b.json");
        File.WriteAllText(target, "old contents that are longer");

        AtomicFile.WriteAllText(target, "new");

        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void LeavesNoTemporaryFileBehind()
    {
        // A reader scanning the directory must not find a second, half-written
        // copy of the config sitting next to the real one.
        var target = PathFor("c.json");

        AtomicFile.WriteAllText(target, "value");

        Assert.Equal(new[] { "c.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void DefaultsToUtf8WithoutABom()
    {
        // config.json is read back by JsonSerializer and by hand; a BOM that
        // appears only after the first save would be a nasty surprise.
        var target = PathFor("d.json");

        AtomicFile.WriteAllText(target, "abc");

        var bytes = File.ReadAllBytes(target);
        Assert.Equal(new byte[] { (byte)'a', (byte)'b', (byte)'c' }, bytes);
    }

    [Fact]
    public void HonoursAnExplicitEncoding()
    {
        var target = PathFor("e.txt");

        AtomicFile.WriteAllText(target, "ก", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var bytes = File.ReadAllBytes(target);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public void RoundTripsNonAsciiContent()
    {
        var target = PathFor("f.json");
        const string content = "{\"title\":\"ตอนที่ 84 — 日本語\"}";

        AtomicFile.WriteAllText(target, content);

        Assert.Equal(content, File.ReadAllText(target, new UTF8Encoding(false)));
    }

    [Fact]
    public void CreatesTheDirectoryWhenItIsMissing()
    {
        var target = Path.Combine(_dir, "nested", "deep", "g.json");

        AtomicFile.WriteAllText(target, "v");

        Assert.Equal("v", File.ReadAllText(target));
    }

    [Fact]
    public void AnExistingFileSurvivesAFailedWrite()
    {
        // The point of the whole exercise: a save that cannot complete must
        // leave the previous good copy in place rather than a truncated one.
        var target = PathFor("h.json");
        File.WriteAllText(target, "{\"good\":true}");

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(target, null!));

        Assert.Equal("{\"good\":true}", File.ReadAllText(target));
    }
}
