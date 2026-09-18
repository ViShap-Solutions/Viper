namespace ViShap.Viper.Security;

internal sealed class BudgetedWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _resourceName;
    private readonly bool _leaveOpen;
    private long _highWaterMark;

    public BudgetedWriteStream(
        Stream inner,
        long maxBytes,
        string resourceName = "wire",
        bool leaveOpen = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Resource name is required.", nameof(resourceName));

        _maxBytes = maxBytes;
        _resourceName = resourceName;
        _leaveOpen = leaveOpen;

        try
        {
            _highWaterMark = _inner.CanSeek ? _inner.Position : 0;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to get the initial position of the underlying {_resourceName} stream.", ex);
        }

        if (_highWaterMark > _maxBytes)
            throw new BinaryLimitException(
                $"The {_resourceName} stream is already positioned beyond the configured maximum of {_maxBytes} bytes.");
    }

    internal long BytesWritten => _highWaterMark;

    public override bool CanRead => false;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;

    public override long Length
    {
        get
        {
            try
            {
                return _inner.Length;
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    $"Failed to get the length of the underlying {_resourceName} stream.", ex);
            }
        }
    }

    public override long Position
    {
        get
        {
            try
            {
                return _inner.Position;
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    $"Failed to get the position of the underlying {_resourceName} stream.", ex);
            }
        }
        set
        {
            if (value > _maxBytes)
                throw new BinaryLimitException(
                    $"The {_resourceName} byte budget of {_maxBytes} would be exceeded by seeking to position {value}.");

            try
            {
                _inner.Position = value;
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    $"Failed to position the underlying {_resourceName} stream.", ex);
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
            return;

        long start;
        try
        {
            start = _inner.CanSeek ? _inner.Position : _highWaterMark;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to determine the current position of the underlying {_resourceName} stream.", ex);
        }

        long endPosition;
        try
        {
            endPosition = checked(start + buffer.Length);
        }
        catch (OverflowException)
        {
            throw LimitExceeded();
        }

        if (endPosition > _maxBytes)
            throw LimitExceeded();

        try
        {
            _inner.Write(buffer);
            _highWaterMark = Math.Max(_highWaterMark, endPosition);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to write {_resourceName} data to the underlying stream.", ex);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(Span<byte> buffer) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
    {
        long basePosition;
        try
        {
            basePosition = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _inner.Position,
                SeekOrigin.End => _inner.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to determine the target position of the underlying {_resourceName} stream.", ex);
        }

        long target;
        try
        {
            target = checked(basePosition + offset);
        }
        catch (OverflowException)
        {
            throw LimitExceeded();
        }

        if (target < 0 || target > _maxBytes)
            throw LimitExceeded();

        try
        {
            return _inner.Seek(offset, origin);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to seek the underlying {_resourceName} stream.", ex);
        }
    }

    public override void SetLength(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (value > _maxBytes)
            throw LimitExceeded();

        try
        {
            _inner.SetLength(value);
            _highWaterMark = Math.Max(_highWaterMark, value);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to change the length of the underlying {_resourceName} stream.", ex);
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    private BinaryLimitException LimitExceeded() =>
        new($"The {_resourceName} byte budget of {_maxBytes} would be exceeded.");
}
