using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// The output of a compression phase, handed to the algorithm as its <see cref="IBufferWriter{T}"/>.
/// It is one contiguous pooled array that grows only as bytes are actually produced and never past
/// its ceiling, so the algorithm can neither exceed the phase's size nor make the serializer allocate
/// for output that does not exist.
/// <para>
/// Decompression grows in one step: a probe rented from the pool, then a buffer of the declared
/// length, which the ratio and payload limits have already bounded; reaching past the probe means the
/// payload is genuinely that large. Compression doubles, since the size of its output is not known.
/// The result leaves this type without a copy, and every array is cleared before it goes back to the
/// pool.
/// </para>
/// </summary>
internal sealed class CompressionBuffer : IBufferWriter<byte>, IDisposable
{
    private const int ProbeCapacity = 64 * 1024;
    private const int MinimumCompressionCapacity = 256;

    private readonly int _ceiling;
    private readonly long _limit;
    private readonly bool _decompressing;
    private byte[]? _buffer;
    private int _written;

    private CompressionBuffer(int ceiling, long limit, bool decompressing, int initialCapacity)
    {
        _ceiling = ceiling;
        _limit = limit;
        _decompressing = decompressing;
        _buffer = RentedBytes.RentArray(initialCapacity);
    }

    /// <summary>
    /// A buffer for decompressed output of exactly <paramref name="expectedLength"/> bytes; asking for
    /// more is malformed data.
    /// </summary>
    public static CompressionBuffer ForDecompression(int expectedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);

        return new CompressionBuffer(
            expectedLength, expectedLength, decompressing: true, Math.Min(expectedLength, ProbeCapacity));
    }

    /// <summary>
    /// A buffer for the compressed form of <paramref name="inputLength"/> bytes; asking for more than
    /// <paramref name="maxCompressedBytes"/> is a limit breach.
    /// </summary>
    public static CompressionBuffer ForCompression(int inputLength, long maxCompressedBytes)
    {
        int ceiling = (int)Math.Min(maxCompressedBytes, Array.MaxLength);
        int initial = Math.Min(ceiling, Math.Max(inputLength, MinimumCompressionCapacity));

        return new CompressionBuffer(ceiling, maxCompressedBytes, decompressing: false, initial);
    }

    public int WrittenCount => _written;

    public void Advance(int count)
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);

        if (count < 0 || _written + count > _buffer.Length || _written + count > _ceiling)
            throw new ArgumentOutOfRangeException(nameof(count));

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written, Available);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written, Available);
    }

    /// <summary>Hands over the output; this buffer owns nothing afterwards.</summary>
    public RentedBytes Detach()
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(CompressionBuffer));
        _buffer = null;

        return RentedBytes.Adopt(buffer, _written);
    }

    /// <summary>Releases the buffer, clearing whatever was not handed over.</summary>
    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;

        if (buffer is not null)
            RentedBytes.ReturnArray(buffer);
    }

    /// <summary>The space the algorithm may still use: what the array holds, up to the ceiling.</summary>
    private int Available => Math.Min(_buffer!.Length, _ceiling) - _written;

    private void Ensure(int sizeHint)
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);

        int required = Math.Max(sizeHint, 1);
        if (Available >= required)
            return;

        if ((long)_written + required > _ceiling)
            throw _decompressing
                ? new BinaryFormatException(
                    "Decompression produced more data than the declared uncompressed length.")
                : new BinaryLimitException(
                    $"Compressed payload could not fit within the configured maximum of {_limit} bytes " +
                    $"({nameof(SerializationLimits.MaxCompressedBytes)}).");

        Grow(required);
    }

    private void Grow(int required)
    {
        int capacity = _decompressing
            ? _ceiling
            : (int)Math.Min(_ceiling, Math.Max((long)_buffer!.Length * 2, (long)_written + required));

        var grown = RentedBytes.RentArray(capacity);
        _buffer.AsSpan(0, _written).CopyTo(grown);
        RentedBytes.ReturnArray(_buffer!);

        _buffer = grown;
    }
}
