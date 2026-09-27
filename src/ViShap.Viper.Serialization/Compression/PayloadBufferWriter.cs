using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// The output buffer of an incremental decompression. It starts small and grows only as bytes are
/// actually produced, so a payload that declares a large uncompressed length never causes the
/// allocation it describes until that many bytes exist.
/// <para>
/// Growth happens exactly once: a probe buffer rented from the pool, then a pooled buffer of the
/// declared size. The payload leaves this type as that buffer, without a copy, and a decompression
/// that never fills the probe never takes more than it. Every buffer is cleared before it goes back
/// to the pool.
/// </para>
/// </summary>
internal sealed class PayloadBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int ProbeCapacity = 64 * 1024;

    private readonly int _capacity;
    private byte[]? _buffer;
    private int _limit;
    private int _written;

    /// <param name="capacity">The declared uncompressed length; output may never exceed it.</param>
    public PayloadBufferWriter(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _limit = Math.Min(capacity, ProbeCapacity);
        _buffer = RentedBytes.RentArray(_limit);
    }

    public int WrittenCount => _written;

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _limit)
            throw new ArgumentOutOfRangeException(nameof(count));

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written, _limit - _written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written, _limit - _written);
    }

    /// <summary>
    /// Hands over the decompressed payload. Valid only when exactly the declared number of bytes was
    /// produced, which is the only outcome the caller accepts.
    /// </summary>
    public RentedBytes DetachPayload()
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PayloadBufferWriter));
        _buffer = null;
        _limit = 0;

        return RentedBytes.Adopt(buffer, _written);
    }

    /// <summary>Releases the buffer, clearing whatever plaintext was not handed over.</summary>
    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;
        _limit = 0;

        if (buffer is not null)
            RentedBytes.ReturnArray(buffer);
    }

    private void Ensure(int sizeHint)
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);

        int required = Math.Max(sizeHint, 1);
        if (_limit - _written >= required)
            return;

        if (_written + required > _capacity)
            throw new BinaryFormatException(
                "Decompression produced more data than the declared uncompressed length.");

        Grow();
    }

    /// <summary>
    /// Promotes the probe to the buffer of the declared size. There is nothing to gain from doubling:
    /// the declared length is the ceiling, and reaching past the probe means the payload is genuinely
    /// that large.
    /// </summary>
    private void Grow()
    {
        var final = RentedBytes.RentArray(_capacity);
        _buffer.AsSpan(0, _written).CopyTo(final);
        RentedBytes.ReturnArray(_buffer!);

        _buffer = final;
        _limit = _capacity;
    }
}
