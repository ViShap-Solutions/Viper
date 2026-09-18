namespace ViShap.Viper.Security;

/// <summary>
/// Metering mechanism, write direction: counts the bytes this operation produces, relative to the
/// destination's starting position. Appending to a stream that already holds data therefore costs
/// the operation nothing. Rewinds used for keyed length back-patching do not double-charge, because
/// the budget follows the high-water mark.
/// </summary>
internal sealed class MeteredWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _resourceName;
    private readonly long _origin;
    private long _cursor;
    private long _highWaterMark;

    public MeteredWriteStream(Stream inner, long maxBytes, string resourceName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        _maxBytes = maxBytes;
        _resourceName = resourceName;

        try
        {
            _origin = _inner.CanSeek ? _inner.Position : 0;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to read the initial position of the {resourceName} stream.", ex);
        }

        _cursor = _origin;
        _highWaterMark = _origin;
    }

    /// <summary>Bytes produced by this operation.</summary>
    public long BytesWritten => _highWaterMark - _origin;

    public override bool CanRead => false;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => true;
    public override long Length => _highWaterMark - _origin;

    public override long Position
    {
        get => _cursor - _origin;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            MoveTo(checked(_origin + value));
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
            return;

        long end;
        try
        {
            end = checked(_cursor + buffer.Length);
        }
        catch (OverflowException)
        {
            throw BudgetExceeded();
        }

        if (end - _origin > _maxBytes)
            throw BudgetExceeded();

        try
        {
            _inner.Write(buffer);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to write {_resourceName} data to the underlying stream.", ex);
        }

        _cursor = end;
        if (end > _highWaterMark)
            _highWaterMark = end;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => _origin + offset,
            SeekOrigin.Current => _cursor + offset,
            SeekOrigin.End => _highWaterMark + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        MoveTo(target);
        return _cursor - _origin;
    }

    public override void Flush()
    {
        try
        {
            _inner.Flush();
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to flush {_resourceName} data to the underlying stream.", ex);
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(Span<byte> buffer) => throw new NotSupportedException();

    private void MoveTo(long absolutePosition)
    {
        if (!_inner.CanSeek)
            throw new NotSupportedException(
                $"The {_resourceName} stream does not support seeking.");

        if (absolutePosition < _origin)
            throw new BinaryStreamException(
                $"Cannot position the {_resourceName} stream before the start of the operation.");

        if (absolutePosition - _origin > _maxBytes)
            throw BudgetExceeded();

        try
        {
            _inner.Position = absolutePosition;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to position the {_resourceName} stream.", ex);
        }

        _cursor = absolutePosition;
        if (absolutePosition > _highWaterMark)
            _highWaterMark = absolutePosition;
    }

    private BinaryLimitException BudgetExceeded() =>
        new($"The {_resourceName} byte budget of {_maxBytes} would be exceeded.");
}
