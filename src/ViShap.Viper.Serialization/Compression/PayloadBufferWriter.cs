using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// The output buffer of an incremental decompression. It starts small and grows only as bytes are
/// actually produced, so a payload that declares a large uncompressed length never causes the
/// allocation it describes until that many bytes exist.
/// <para>
/// Growth happens exactly once: a probe buffer rented from the pool, then the final array of the
/// declared size. The payload therefore leaves this type without a copy, and a decompression that
/// never fills the probe never allocates more than it.
/// </para>
/// </summary>
internal sealed class PayloadBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int ProbeCapacity = 64 * 1024;

    private readonly int _capacity;
    private byte[] _buffer;
    private int _limit;
    private bool _pooled;
    private bool _detached;
    private int _written;

    /// <param name="capacity">The declared uncompressed length; output may never exceed it.</param>
    public PayloadBufferWriter(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;

        if (capacity <= ProbeCapacity)
        {
            _buffer = capacity == 0 ? [] : new byte[capacity];
            _limit = capacity;
            return;
        }

        _buffer = ArrayPool<byte>.Shared.Rent(ProbeCapacity);
        _limit = ProbeCapacity;
        _pooled = true;
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
    public byte[] DetachPayload()
    {
        _detached = true;
        return _buffer.Length == _capacity ? _buffer : _buffer.AsSpan(0, _written).ToArray();
    }

    /// <summary>Releases the buffer, clearing whatever plaintext was not handed over.</summary>
    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        _limit = 0;

        if (_pooled)
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            return;
        }

        if (!_detached)
            Array.Clear(buffer);
    }

    private void Ensure(int sizeHint)
    {
        int required = Math.Max(sizeHint, 1);
        if (_limit - _written >= required)
            return;

        if (_written + required > _capacity)
            throw new BinaryFormatException(
                "Decompression produced more data than the declared uncompressed length.");

        Grow();
    }

    /// <summary>
    /// Promotes the probe to the final array. There is nothing to gain from doubling: the declared
    /// length is the ceiling, and reaching past the probe means the payload is genuinely that large.
    /// </summary>
    private void Grow()
    {
        var final = new byte[_capacity];
        _buffer.AsSpan(0, _written).CopyTo(final);

        if (_pooled)
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);

        _buffer = final;
        _limit = _capacity;
        _pooled = false;
    }
}
