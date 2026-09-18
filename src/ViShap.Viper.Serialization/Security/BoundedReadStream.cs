namespace ViShap.Viper.Security;

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _length;
    private readonly string _resourceName;
    private readonly bool _leaveOpen;
    private long _position;

    public BoundedReadStream(
        Stream inner,
        long length,
        string resourceName,
        bool leaveOpen = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Resource name is required.", nameof(resourceName));

        _length = length;
        _resourceName = resourceName;
        _leaveOpen = leaveOpen;
    }

    internal long Remaining => _length - _position;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException(
            $"{nameof(BoundedReadStream)} does not support seeking.");
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || Remaining == 0)
            return 0;

        int request = (int)Math.Min(buffer.Length, Remaining);
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
                $"Failed to read {_resourceName} from the underlying stream.", ex);
        }
    }

    public override int ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        int read = Read(buffer);
        return read == 0 ? -1 : buffer[0];
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
