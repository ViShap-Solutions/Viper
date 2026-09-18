namespace ViShap.Viper.Security;

internal sealed class BudgetedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _resourceName;
    private readonly bool _leaveOpen;
    private long _bytesRead;

    public BudgetedReadStream(
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
    }

    internal long BytesRead => _bytesRead;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;

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

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return 0;

        long remaining = _maxBytes - _bytesRead;
        if (buffer.Length > remaining)
            throw new BinaryLimitException(
                $"The {_resourceName} byte budget of {_maxBytes} would be exceeded by reading {buffer.Length} more bytes.");

        try
        {
            int read = _inner.Read(buffer);
            if (read > 0)
                _bytesRead = checked(_bytesRead + read);
            return read;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to read {_resourceName} data from the underlying stream.", ex);
        }
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        int read = Read(buffer);
        return read == 0 ? -1 : buffer[0];
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
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
        try
        {
            _inner.SetLength(value);
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
                $"Failed to flush the underlying {_resourceName} stream.", ex);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
