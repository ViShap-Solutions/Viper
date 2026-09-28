using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// A write-only stream that appends to an <see cref="IBufferWriter{T}"/>, for a codec that is only
/// available as a <see cref="Stream"/>. It holds no bytes of its own.
/// </summary>
internal sealed class BufferWriterStream(IBufferWriter<byte> destination) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(ReadOnlySpan<byte> buffer) => destination.Write(buffer);

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void WriteByte(byte value) => Write([value]);

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// A read-only stream over the first bytes of an array, for a codec that is only available as a
/// <see cref="Stream"/>. The array stays its owner's; the stream never clears or returns it.
/// </summary>
internal sealed class ArrayReadStream(byte[] array, int length) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        int count = Math.Min(buffer.Length, length - _position);
        array.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int ReadByte() => _position < length ? array[_position++] : -1;

    public override void Flush() { }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
