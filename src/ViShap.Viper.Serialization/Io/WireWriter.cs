using System.Buffers.Binary;
using System.Text;

namespace ViShap.Viper.Io;

/// <summary>
/// The only way to write bytes of a payload. It writes into the serializer's own
/// <see cref="PayloadBuffer"/> through a span it holds between calls, so a primitive is a store into
/// memory and the buffer is consulted only when that span is used up. Sizes are validated against
/// the same limits as on the way in, so a graph that could not be read back is rejected while it is
/// written instead of producing an unreadable payload.
/// <para>
/// The writer is a <see langword="ref struct"/> passed by reference: a copy would hold a stale span.
/// Bytes it has written reach the buffer at the latest on <see cref="Flush"/>.
/// </para>
/// </summary>
internal ref struct WireWriter
{
    private readonly ref OperationState _state;
    private readonly PayloadBuffer _buffer;
    private Span<byte> _span;
    private int _buffered;

    public WireWriter(PayloadBuffer buffer, ref OperationState state)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _state = ref state;
        _span = default;
        _buffered = 0;
    }

    /// <summary>The state of the operation these bytes belong to.</summary>
    internal readonly ref OperationState State => ref _state;

    /// <summary>Bytes written so far, counted from the start of the buffer.</summary>
    public readonly long Position => _buffer.Length + _buffered;

    public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteByte(byte value)
    {
        if (_buffered == _span.Length)
            Grow(1);

        _span[_buffered++] = value;
    }

    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    public void WriteInt16(short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(Reserve(sizeof(short)), value);

    public void WriteUInt16(ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(Reserve(sizeof(ushort)), value);

    public void WriteChar(char value) => WriteUInt16(value);

    public void WriteInt32(int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(Reserve(sizeof(int)), value);

    public void WriteUInt32(uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(Reserve(sizeof(uint)), value);

    public void WriteInt64(long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(Reserve(sizeof(long)), value);

    public void WriteUInt64(ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(Reserve(sizeof(ulong)), value);

    public void WriteSingle(float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(Reserve(sizeof(float)), value);

    public void WriteDouble(double value) =>
        BinaryPrimitives.WriteDoubleLittleEndian(Reserve(sizeof(double)), value);

    public void WriteDecimal(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        var destination = Reserve(16);
        BinaryPrimitives.WriteInt32LittleEndian(destination, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], bits[2]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], bits[3]);
    }

    /// <summary>
    /// Writes raw bytes. What does not fit the current segment goes into one new segment sized for
    /// all of it, so a large blob costs one segment rather than a chain grown step by step.
    /// </summary>
    public void Write(scoped ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (_buffered == _span.Length)
                Grow(bytes.Length);

            int take = Math.Min(bytes.Length, _span.Length - _buffered);
            bytes[..take].CopyTo(_span[_buffered..]);
            _buffered += take;
            bytes = bytes[take..];
        }
    }

    /// <summary>Writes a length-prefixed byte blob bounded by <c>MaxByteBlobBytes</c>.</summary>
    public void WriteBlob(scoped ReadOnlySpan<byte> bytes, string what)
    {
        if (bytes.Length > _state.Limits.MaxByteBlobBytes)
            throw new BinaryLimitException(
                $"{what} byte length {bytes.Length} exceeds the configured maximum of " +
                $"{_state.Limits.MaxByteBlobBytes} (MaxByteBlobBytes).");

        Write7BitEncodedInt(bytes.Length);
        Write(bytes);
    }

    /// <summary>Writes a UTF-8 string bounded by <c>MaxStringBytes</c>.</summary>
    /// <exception cref="BinaryLimitException">The encoded length exceeds the configured maximum.</exception>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > _state.Limits.MaxStringBytes)
            throw new BinaryLimitException(
                $"String byte length {byteCount} exceeds the configured maximum of " +
                $"{_state.Limits.MaxStringBytes} (MaxStringBytes).");

        WriteEncodedString(value, byteCount);
    }

    /// <summary>
    /// Writes a UTF-8 string that must fit a ceiling the format itself fixes, such as a header field.
    /// </summary>
    /// <param name="value">The string to write.</param>
    /// <param name="maxBytes">The largest encoded length the format admits here.</param>
    /// <param name="what">The field being written, used in diagnostics.</param>
    /// <exception cref="BinaryConfigurationException">
    /// The encoded length exceeds <paramref name="maxBytes"/>, so the value cannot be represented.
    /// </exception>
    public void WriteString(string value, int maxBytes, string what)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > maxBytes)
            throw new BinaryConfigurationException(
                $"{what} encodes to {byteCount} byte(s), but this field admits at most {maxBytes}.");

        WriteEncodedString(value, byteCount);
    }

    /// <summary>Validates a count against its limit and the element budget, then writes it.</summary>
    public ElementCount WriteCount(int count, CountKind kind, string what)
    {
        var validated = ElementCount.Validate(count, kind, ref _state, what);
        WriteInt32(validated.Value);
        return validated;
    }

    /// <summary>Validates a bit count bounded by <c>MaxByteBlobBytes</c> × 8, then writes it.</summary>
    public void WriteBitCount(int bits, string what)
    {
        if (bits < 0)
            throw new BinaryFormatException($"{what} {bits} must be non-negative.");

        long maximum = (long)_state.Limits.MaxByteBlobBytes * 8L;
        if (bits > maximum)
            throw new BinaryLimitException(
                $"{what} {bits} exceeds the configured maximum of {maximum} " +
                $"(MaxByteBlobBytes, in bits).");

        WriteInt32(bits);
    }

    public void Write7BitEncodedInt(int value)
    {
        if (value < 0)
            throw new BinaryFormatException($"7-bit encoded integer {value} must be non-negative.");

        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            WriteByte((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        WriteByte((byte)remaining);
    }

    /// <summary>
    /// Overwrites an <see cref="int"/> written earlier at <paramref name="position"/>, such as a length
    /// reserved before the bytes it describes were known.
    /// </summary>
    public void PatchInt32(long position, int value)
    {
        Flush();

        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _buffer.Patch(position, bytes);
    }

    /// <summary>Commits every byte written so far to the buffer.</summary>
    public void Flush()
    {
        if (_buffered > 0)
            _buffer.Advance(_buffered);

        _span = default;
        _buffered = 0;
    }

    private void WriteEncodedString(string value, int byteCount)
    {
        Write7BitEncodedInt(byteCount);
        if (byteCount == 0)
            return;

        Encoding.UTF8.GetBytes(value, Reserve(byteCount));
    }

    /// <summary>Hands out the next <paramref name="size"/> bytes of contiguous space.</summary>
    private Span<byte> Reserve(int size)
    {
        if (_span.Length - _buffered < size)
            Grow(size);

        var reserved = _span.Slice(_buffered, size);
        _buffered += size;
        return reserved;
    }

    private void Grow(int size)
    {
        Flush();
        _span = _buffer.GetSpan(size);
    }
}
