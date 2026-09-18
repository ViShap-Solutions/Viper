namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>A readable stream that cannot seek, for the APIs that require seekability.</summary>
internal sealed class NonSeekableStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A seekable stream that never returns more than <paramref name="chunkSize"/> bytes per read, the
/// way a network or pipe stream behaves.
/// </summary>
internal sealed class PartialReadStream(byte[] content, int chunkSize) : Stream
{
    private readonly MemoryStream _inner = new(content, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) =>
        _inner.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A stream that raises <see cref="IOException"/> once the operation has moved
/// <paramref name="bytesBeforeFailure"/> bytes, so a test can prove where I/O failures are attributed.
/// </summary>
internal sealed class FailingStream(int bytesBeforeFailure, bool canSeek = true) : Stream
{
    private long _moved;

    public override bool CanRead => true;
    public override bool CanSeek => canSeek;
    public override bool CanWrite => true;
    public override long Length => long.MaxValue;

    public override long Position
    {
        get => _moved;
        set => _moved = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        Advance(buffer.Length);
        buffer.Clear();
        return buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer) => Advance(buffer.Length);

    private void Advance(int count)
    {
        if (_moved + count > bytesBeforeFailure)
            throw new IOException("The underlying device reported a failure.");

        _moved += count;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => _moved;
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>A writable stream that cannot seek, such as a network or console destination.</summary>
internal sealed class NonSeekableWriteStream : Stream
{
    private readonly MemoryStream _inner = new();

    public byte[] Written => _inner.ToArray();
    public bool Disposed { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>A seekable stream that records whether the serializer disposed it.</summary>
internal sealed class TrackingStream(byte[]? content = null) : Stream
{
    private readonly MemoryStream _inner = content is null ? new MemoryStream() : new MemoryStream(content);

    public bool Disposed { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
