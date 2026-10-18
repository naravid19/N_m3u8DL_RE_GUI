#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Tests.Fixtures;

/// <summary>
/// Yields <paramref name="bytesBeforeFailure"/> bytes and then throws, modelling
/// a connection dropped mid-body — the case that makes a retry happen at all.
/// </summary>
public sealed class FailsPartwayStream : Stream
{
    private readonly byte[] _payload;
    private readonly int _bytesBeforeFailure;
    private int _position;

    public FailsPartwayStream(byte[] payload, int bytesBeforeFailure)
    {
        _payload = payload;
        _bytesBeforeFailure = bytesBeforeFailure;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _bytesBeforeFailure)
            throw new IOException("connection reset by peer (simulated)");

        var take = Math.Min(count, _bytesBeforeFailure - _position);
        Array.Copy(_payload, _position, buffer, offset, take);
        _position += take;
        return take;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _bytesBeforeFailure)
            throw new IOException("connection reset by peer (simulated)");

        var take = Math.Min(buffer.Length, _bytesBeforeFailure - _position);
        _payload.AsSpan(_position, take).CopyTo(buffer.Span);
        _position += take;
        return ValueTask.FromResult(take);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _payload.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Yields the payload in fixed slices with a delay between each, so the
/// service's 200 ms progress reporter is guaranteed to observe the download
/// in flight rather than only after it has finished.
/// </summary>
public sealed class SlowStream : Stream
{
    private readonly byte[] _payload;
    private readonly int _sliceSize;
    private readonly TimeSpan _delayPerSlice;
    private int _position;

    public SlowStream(byte[] payload, int sliceSize, TimeSpan delayPerSlice)
    {
        _payload = payload;
        _sliceSize = sliceSize;
        _delayPerSlice = delayPerSlice;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _payload.Length)
            return 0;

        await Task.Delay(_delayPerSlice, cancellationToken).ConfigureAwait(false);

        var take = Math.Min(Math.Min(buffer.Length, _sliceSize), _payload.Length - _position);
        _payload.AsSpan(_position, take).CopyTo(buffer.Span);
        _position += take;
        return take;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _payload.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
