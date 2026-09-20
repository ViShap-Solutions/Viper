namespace ViShap.Viper.Security;

/// <summary>
/// Metering mechanism, read direction: counts the bytes this operation consumes from a caller-owned
/// stream and refuses to exceed the configured budget. The count starts at zero regardless of where
/// the caller's stream happens to be positioned.
/// </summary>
internal sealed class MeteredReadStream(
    Stream inner,
    long maxBytes,
    string resourceName) : Stream, IRemainingBytes
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly long _maxBytes = maxBytes > 0
        ? maxBytes
        : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    private long _bytesRead;

    /// <summary>
    /// What a declared length may still claim: the lesser of the operation's remaining budget and the
    /// bytes the source can physically still yield.
    /// </summary>
    public long RemainingBytes
    {
        get
        {
            long budget = BudgetRemaining;
            return PhysicalRemaining is { } physical && physical < budget ? physical : budget;
        }
    }

    public long BytesRead => _bytesRead;

    private long BudgetRemaining => _maxBytes - _bytesRead;

    /// <summary>
    /// What the source can still deliver, or <see langword="null"/> when it cannot say. A nested meter
    /// answers for itself, so the physical truth propagates through a chain of them.
    /// </summary>
    private long? PhysicalRemaining => _inner switch
    {
        IRemainingBytes bounded => bounded.RemainingBytes,
        { CanSeek: true } => Math.Max(0, _inner.Length - _inner.Position),
        _ => null
    };

    /// <summary>
    /// Classifies a declaration this source cannot satisfy. Exceeding the operation budget is a limit
    /// violation; fitting the budget but not the remaining bytes means the payload is shorter than it
    /// claims, which is a malformed payload.
    /// </summary>
    public BinaryFormatException Exceeded(long requested, string what) =>
        requested > BudgetRemaining
            ? new BinaryLimitException(
                $"{what} needs {requested} byte(s), which would exceed the {resourceName} byte budget " +
                $"of {_maxBytes}.")
            : new BinaryFormatException(
                $"{what} declares {requested} byte(s) but only {RemainingBytes} remain in the " +
                $"{resourceName} stream.");

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return 0;

        if (buffer.Length > BudgetRemaining)
            throw new BinaryLimitException(
                $"The {resourceName} byte budget of {_maxBytes} would be exceeded by reading " +
                $"{buffer.Length} more bytes.");

        try
        {
            int read = _inner.Read(buffer);
            if (read > 0)
                _bytesRead += read;
            return read;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to read {resourceName} data from the underlying stream.", ex);
        }
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        return Read(buffer) == 0 ? -1 : buffer[0];
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
}
