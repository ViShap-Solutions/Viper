using System.Buffers.Binary;
using System.Text;

namespace ViShap.Viper.Io;

/// <summary>
/// The only way to write bytes of a payload. Mirrors <see cref="ValueReader"/>: sizes are validated
/// against the same limits on the way out, so a graph that could not be read back is rejected while
/// writing instead of producing an unreadable payload.
/// </summary>
internal sealed class ValueWriter(Stream destination, SerializationOperation operation)
{
    private readonly Stream _destination = destination ?? throw new ArgumentNullException(nameof(destination));

    internal SerializationOperation Operation { get; } = operation;

    public bool CanSeek => _destination.CanSeek;

    public long Position
    {
        get => _destination.Position;
        set => _destination.Position = value;
    }

    public void WriteBoolean(bool value) => WriteRawByte(value ? (byte)1 : (byte)0);

    public void WriteByte(byte value) => WriteRawByte(value);

    public void WriteSByte(sbyte value) => WriteRawByte((byte)value);

    public void WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteChar(char value) => WriteUInt16(value);

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteDecimal(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);

        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, bits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], bits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], bits[2]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], bits[3]);
        Write(buffer);
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _destination.Write(bytes);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to write payload data to the underlying stream.", ex);
        }
    }

    /// <summary>Writes a length-prefixed byte blob bounded by <c>MaxByteBlobBytes</c>.</summary>
    public void WriteBlob(ReadOnlySpan<byte> bytes, string what)
    {
        if (bytes.Length > Operation.Limits.MaxByteBlobBytes)
            throw new BinaryLimitException(
                $"{what} byte length {bytes.Length} exceeds the configured maximum of " +
                $"{Operation.Limits.MaxByteBlobBytes}.");

        Write7BitEncodedInt(bytes.Length);
        Write(bytes);
    }

    /// <summary>Writes a UTF-8 string bounded by <c>MaxStringBytes</c>.</summary>
    /// <exception cref="BinaryLimitException">The encoded length exceeds the configured maximum.</exception>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > Operation.Limits.MaxStringBytes)
            throw new BinaryLimitException(
                $"String byte length {byteCount} exceeds the configured maximum of " +
                $"{Operation.Limits.MaxStringBytes}.");

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

    private void WriteEncodedString(string value, int byteCount)
    {
        Write7BitEncodedInt(byteCount);
        if (byteCount == 0)
            return;

        Write(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>Validates a count against its limit and the element budget, then writes it.</summary>
    public ElementCount WriteCount(int count, CountKind kind, string what)
    {
        var validated = ElementCount.Validate(count, kind, Operation, what);
        WriteInt32(validated.Value);
        return validated;
    }

    /// <summary>Validates a bit count bounded by <c>MaxByteBlobBytes</c> × 8, then writes it.</summary>
    public void WriteBitCount(int bits, string what)
    {
        if (bits < 0)
            throw new BinaryFormatException($"{what} {bits} must be non-negative.");

        long maximum = (long)Operation.Limits.MaxByteBlobBytes * 8L;
        if (bits > maximum)
            throw new BinaryLimitException(
                $"{what} {bits} exceeds the configured maximum of {maximum}.");

        WriteInt32(bits);
    }

    public void Write7BitEncodedInt(int value)
    {
        if (value < 0)
            throw new BinaryFormatException($"7-bit encoded integer {value} must be non-negative.");

        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            WriteRawByte((byte)(remaining | 0x80));
            remaining >>= 7;
        }

        WriteRawByte((byte)remaining);
    }

    public void Flush()
    {
        try
        {
            _destination.Flush();
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to flush payload data to the underlying stream.", ex);
        }
    }

    private void WriteRawByte(byte value)
    {
        Span<byte> buffer = [value];
        Write(buffer);
    }
}
