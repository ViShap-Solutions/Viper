namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A stream that cannot seek and holds <paramref name="content"/>, of which only the first
/// <paramref name="boundary"/> bytes may be asked for. A read that asks for any byte past the
/// boundary — even one the stream could serve — fails the test, the way a socket carrying the next
/// message would hand that message's bytes to the wrong reader. It is how a test proves that a reader
/// takes exactly one frame and nothing after it.
/// </summary>
internal sealed class FrameBoundStream(byte[] content, int boundary) : Stream
{
    private int _position;

    /// <summary>How many bytes the reader has taken.</summary>
    public int Taken => _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position + buffer.Length > boundary)
            throw new InvalidOperationException(
                $"A read asked for bytes {_position}…{_position + buffer.Length - 1}, past the frame, " +
                $"which ends at byte {boundary - 1}.");

        int take = Math.Min(buffer.Length, content.Length - _position);
        content.AsSpan(_position, take).CopyTo(buffer);
        _position += take;
        return take;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
