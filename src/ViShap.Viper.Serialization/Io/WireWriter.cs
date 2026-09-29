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

    /// <summary>
    /// Writes a UTF-8 string of the payload bounded by <c>MaxStringBytes</c>: its byte length plus one,
    /// then the bytes. A string in the payload is always of a type that can be null, and its null is
    /// the zero of that length, which the engine writes.
    /// </summary>
    /// <exception cref="BinaryLimitException">The encoded length exceeds the configured maximum.</exception>
    /// <exception cref="BinaryFormatException">The string holds a lone surrogate.</exception>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = EncodedLength(value);
        if (byteCount > _state.Limits.MaxStringBytes)
            throw new BinaryLimitException(
                $"String byte length {byteCount} exceeds the configured maximum of " +
                $"{_state.Limits.MaxStringBytes} (MaxStringBytes).");

        WriteFolded(byteCount, nullFolded: true, "String byte length");
        WriteEncodedBytes(value, byteCount);
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

        int byteCount = EncodedLength(value);
        if (byteCount > maxBytes)
            throw new BinaryConfigurationException(
                $"{what} encodes to {byteCount} byte(s), but this field admits at most {maxBytes}.");

        Write7BitEncodedInt(byteCount);
        WriteEncodedBytes(value, byteCount);
    }

    /// <summary>
    /// Writes a UTF-8 string that may be absent and must fit a ceiling the format itself fixes: its
    /// byte length plus one, or zero when <paramref name="value"/> is <see langword="null"/>, then the
    /// bytes.
    /// </summary>
    /// <exception cref="BinaryConfigurationException">
    /// The encoded length exceeds <paramref name="maxBytes"/>, so the value cannot be represented.
    /// </exception>
    public void WriteOptionalString(string? value, int maxBytes, string what)
    {
        if (value is null)
        {
            WriteByte(0);
            return;
        }

        int byteCount = EncodedLength(value);
        if (byteCount > maxBytes)
            throw new BinaryConfigurationException(
                $"{what} encodes to {byteCount} byte(s), but this field admits at most {maxBytes}.");

        Write7BitEncodedInt(byteCount + 1);
        WriteEncodedBytes(value, byteCount);
    }

    /// <summary>
    /// Validates a count against its limit and the element budget, then writes it — one higher when
    /// <paramref name="nullFolded"/>, because the count is the first number of a value whose null is
    /// its zero.
    /// </summary>
    public ElementCount WriteCount(int count, CountKind kind, string what, bool nullFolded)
    {
        var validated = ElementCount.Validate(count, kind, ref _state, what);
        WriteFolded(validated.Value, nullFolded, what);
        return validated;
    }

    /// <summary>
    /// Validates a bit count bounded by <c>MaxByteBlobBytes</c> × 8, then writes it — one higher when
    /// <paramref name="nullFolded"/>.
    /// </summary>
    public void WriteBitCount(int bits, string what, bool nullFolded)
    {
        if (bits < 0)
            throw new BinaryFormatException($"{what} {bits} must be non-negative.");

        long maximum = (long)_state.Limits.MaxByteBlobBytes * 8L;
        if (bits > maximum)
            throw new BinaryLimitException(
                $"{what} {bits} exceeds the configured maximum of {maximum} " +
                $"(MaxByteBlobBytes, in bits).");

        WriteFolded(bits, nullFolded, what);
    }

    /// <summary>
    /// Writes a structural number, one higher when <paramref name="nullFolded"/>: the first number of a
    /// value whose declared type can be null carries that null as its zero.
    /// </summary>
    /// <exception cref="BinaryLimitException">The number, raised by one, leaves the range a 7-bit integer admits.</exception>
    public void WriteFolded(int value, bool nullFolded, string what)
    {
        if (nullFolded && value == int.MaxValue)
            throw new BinaryLimitException(
                $"{what} {value} cannot be written: one more than it exceeds the largest number the format admits.");

        Write7BitEncodedInt(nullFolded ? value + 1 : value);
    }

    /// <summary>Writes the null of a value whose declared type can be null: a single zero byte.</summary>
    public void WriteNull() => WriteByte(0);

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

    /// <summary>
    /// Encodes strictly: a string that is not well-formed UTF-16 — a lone surrogate — raises instead of
    /// becoming a U+FFFD replacement character, so a value is never written as a different one.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static int EncodedLength(string value)
    {
        try
        {
            return StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new BinaryFormatException(
                "A string holds a lone surrogate, which UTF-8 cannot encode.", ex);
        }
    }

    private void WriteEncodedBytes(string value, int byteCount)
    {
        if (byteCount == 0)
            return;

        StrictUtf8.GetBytes(value, Reserve(byteCount));
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
