namespace ViShap.Viper.Security;

/// <summary>
/// Windowing mechanism: exposes exactly one declared subrange of an already metered stream. A decoder
/// working inside the window cannot read into the next field, and the window always knows how many
/// bytes remain, so a declared length can be rejected before it drives an allocation.
/// </summary>
internal sealed class WindowReadStream(
    Stream inner,
    long length,
    string resourceName) : Stream, IRemainingBytes
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly long _length = length >= 0
        ? length
        : throw new ArgumentOutOfRangeException(nameof(length));
    private long _position;

    public long RemainingBytes => _length - _position;

    /// <summary>Running past a declared window means the payload is shorter than it claims.</summary>
    public BinaryFormatException Exceeded(long requested, string what) =>
        new($"{what} declares {requested} byte(s) but only {RemainingBytes} remain in {resourceName}.");

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || RemainingBytes == 0)
            return 0;

        int request = (int)Math.Min(buffer.Length, RemainingBytes);
        try
        {
            int read = _inner.Read(buffer[..request]);
            if (read > 0)
                _position += read;
            return read;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to read {resourceName} from the underlying stream.", ex);
        }
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        return Read(buffer) == 0 ? -1 : buffer[0];
    }

    /// <summary>Consumes the rest of the window without materializing it.</summary>
    public void SkipRemaining()
    {
        Span<byte> buffer = stackalloc byte[512];
        while (RemainingBytes > 0)
        {
            int read = Read(buffer);
            if (read == 0)
                throw new BinaryFormatException(
                    $"{resourceName} ended early: {RemainingBytes} declared byte(s) are missing.");
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
}
