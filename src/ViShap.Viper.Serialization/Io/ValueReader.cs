using System.Buffers.Binary;
using System.Text;

namespace ViShap.Viper.Io;

/// <summary>A bounded view over one declared field of a payload.</summary>
internal readonly struct PayloadWindow(WindowReadStream stream, ValueReader reader)
{
    public WindowReadStream Stream { get; } = stream;
    public ValueReader Reader { get; } = reader;
}

/// <summary>
/// The only way to read bytes of a payload. Every primitive is checked: a truncated stream produces
/// <see cref="BinaryFormatException"/> instead of a framework exception, a declared length is
/// compared against both its configured maximum and the bytes that physically remain before any
/// allocation happens, and a count can only be obtained as a validated <see cref="ElementCount"/>.
/// Formatters hold one of these and nothing else, so there is no unchecked path to the wire.
/// </summary>
internal sealed class ValueReader(Stream source, SerializationOperation operation)
{
    private readonly Stream _source = source ?? throw new ArgumentNullException(nameof(source));

    internal SerializationOperation Operation { get; } = operation;

    /// <summary>Bytes still physically available, when the source can tell.</summary>
    public long? RemainingBytes => _source switch
    {
        IRemainingBytes bounded => bounded.RemainingBytes,
        { CanSeek: true } => _source.Length - _source.Position,
        _ => null
    };

    public long? Position => _source.CanSeek ? _source.Position : null;

    /// <summary>
    /// Reads a boolean. The wire admits exactly two encodings, so any other byte is a malformed
    /// payload rather than a second spelling of <see langword="true"/> — which is what keeps a flag
    /// unforgeable under authenticated encryption, where the tag covers the decoded fields.
    /// </summary>
    public bool ReadBoolean() => ReadOneByte("Boolean") switch
    {
        0 => false,
        1 => true,
        var other => throw new BinaryFormatException(
            $"Boolean value {other} is not a valid encoding; only 0 and 1 are admitted.")
    };

    public byte ReadByte() => ReadOneByte("Byte");

    public sbyte ReadSByte() => (sbyte)ReadOneByte("SByte");

    public short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExact(buffer, "Int16");
        return BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    public ushort ReadUInt16()
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExact(buffer, "UInt16");
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    public char ReadChar() => (char)ReadUInt16();

    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(buffer, "Int32");
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public uint ReadUInt32()
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(buffer, "UInt32");
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExact(buffer, "Int64");
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public ulong ReadUInt64()
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExact(buffer, "UInt64");
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    public float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(buffer, "Single");
        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExact(buffer, "Double");
        return BinaryPrimitives.ReadDoubleLittleEndian(buffer);
    }

    public decimal ReadDecimal()
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExact(buffer, "Decimal");

        Span<int> bits =
        [
            BinaryPrimitives.ReadInt32LittleEndian(buffer),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(buffer[12..])
        ];

        try
        {
            return new decimal(bits);
        }
        catch (ArgumentException ex)
        {
            throw new BinaryFormatException(
                "Decimal value is malformed.", ex);
        }
    }

    /// <summary>Fills <paramref name="destination"/> completely or fails.</summary>
    public void ReadExact(Span<byte> destination, string what)
    {
        RequireAvailable(destination.Length, what);

        int offset = 0;
        while (offset < destination.Length)
        {
            int read;
            try
            {
                read = _source.Read(destination[offset..]);
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    $"Failed to read {what} from the underlying stream.", ex);
            }

            if (read == 0)
                throw new BinaryFormatException(
                    $"{what} ended early. Expected {destination.Length} bytes, got {offset}.");

            offset += read;
        }
    }

    /// <summary>Reads a length-prefixed byte blob bounded by <c>MaxByteBlobBytes</c>.</summary>
    public byte[] ReadBlob(string what)
    {
        int length = ReadBoundedLength(Operation.Limits.MaxByteBlobBytes, $"{what} byte length");
        return ReadBytes(length, what);
    }

    /// <summary>Reads a UTF-8 string bounded by <c>MaxStringBytes</c>.</summary>
    /// <exception cref="BinaryLimitException">The declared length exceeds the configured maximum.</exception>
    public string ReadString()
    {
        int length = ReadBoundedLength(Operation.Limits.MaxStringBytes, "String byte length");
        return DecodeString(length, "String");
    }

    /// <summary>
    /// Reads a UTF-8 string whose declared byte length must fit a ceiling the format itself fixes,
    /// such as a header field. Exceeding it describes malformed input rather than a policy breach, so
    /// it is reported as a format error and not as a limit violation.
    /// </summary>
    /// <param name="maxBytes">The largest encoded length the format admits here.</param>
    /// <param name="what">The field being read, used in diagnostics.</param>
    /// <exception cref="BinaryFormatException">The declared length exceeds <paramref name="maxBytes"/>.</exception>
    public string ReadString(int maxBytes, string what)
    {
        int length = Read7BitEncodedInt($"{what} byte length");
        if (length > maxBytes)
            throw new BinaryFormatException(
                $"{what} declares {length} byte(s), but this field admits at most {maxBytes}.");

        RequireAvailable(length, what);
        return DecodeString(length, what);
    }

    private string DecodeString(int length, string what)
    {
        if (length == 0)
            return string.Empty;

        byte[] bytes = ReadBytes(length, what);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch (ArgumentException ex)
        {
            throw new BinaryFormatException($"{what} payload is not valid UTF-8.", ex);
        }
    }

    /// <summary>
    /// Reads an element count, validating it against its limit and charging the element budget.
    /// </summary>
    public ElementCount ReadCount(CountKind kind, string what) =>
        ElementCount.Validate(ReadInt32(), kind, Operation, what);

    /// <summary>Reads a bit count bounded by <c>MaxByteBlobBytes</c> × 8.</summary>
    public int ReadBitCount(string what)
    {
        int bits = ReadInt32();
        if (bits < 0)
            throw new BinaryFormatException($"{what} {bits} must be non-negative.");

        if (bits > (long)Operation.Limits.MaxByteBlobBytes * 8L)
            throw new BinaryLimitException(
                $"{what} {bits} exceeds the configured maximum of " +
                $"{(long)Operation.Limits.MaxByteBlobBytes * 8L}.");

        return bits;
    }

    /// <summary>Reads a non-negative 7-bit encoded integer, rejecting malformed encodings.</summary>
    public int Read7BitEncodedInt(string what)
    {
        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte current = ReadOneByte($"{what} 7-bit integer");

            if (shift == 28 && (current & 0xF0) != 0)
                throw new BinaryFormatException($"Malformed {what} 7-bit integer.");

            result |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                if (result > int.MaxValue)
                    throw new BinaryFormatException(
                        $"{what} value {result} is outside the supported non-negative Int32 range.");

                return (int)result;
            }
        }

        throw new BinaryFormatException($"Malformed {what} 7-bit integer.");
    }

    /// <summary>
    /// Rejects a declared length that cannot possibly be satisfied, before it is used to allocate.
    /// </summary>
    public void RequireAvailable(long length, string what)
    {
        if (length < 0)
            throw new BinaryFormatException($"{what} length {length} must be non-negative.");

        switch (_source)
        {
            case IRemainingBytes bounded when length > bounded.RemainingBytes:
                throw bounded.Exceeded(length, what);

            case { CanSeek: true } when length > _source.Length - _source.Position:
                throw new BinaryFormatException(
                    $"{what} declares {length} byte(s) but only " +
                    $"{_source.Length - _source.Position} remain in the payload.");
        }
    }

    /// <summary>
    /// Consumes <paramref name="length"/> bytes in bounded chunks. Skipping an unknown keyed field
    /// never materializes an attacker-sized buffer.
    /// </summary>
    public void Skip(long length, string what)
    {
        RequireAvailable(length, what);

        Span<byte> buffer = stackalloc byte[512];
        long remaining = length;
        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read;
            try
            {
                read = _source.Read(buffer[..request]);
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    $"Failed to skip {what} in the underlying stream.", ex);
            }

            if (read == 0)
                throw new BinaryFormatException(
                    $"{what} ended early. Expected {length} bytes, got {length - remaining}.");

            remaining -= read;
        }
    }

    /// <summary>
    /// Opens a reader over exactly <paramref name="length"/> bytes of this payload. The inner reader
    /// shares the operation, so budgets stay cumulative, but it cannot read past the declared field.
    /// </summary>
    public PayloadWindow OpenWindow(int length, string what)
    {
        RequireAvailable(length, what);
        var window = new WindowReadStream(_source, length, what);
        return new PayloadWindow(window, new ValueReader(window, Operation));
    }

    internal byte[] ReadBytes(int length, string what)
    {
        if (length == 0)
            return [];

        RequireAvailable(length, what);
        byte[] result = new byte[length];
        ReadExact(result, what);
        return result;
    }

    private int ReadBoundedLength(long maximum, string what)
    {
        int length = Read7BitEncodedInt(what);
        if (length > maximum)
            throw new BinaryLimitException(
                $"{what} {length} exceeds the configured maximum of {maximum}.");

        RequireAvailable(length, what);
        return length;
    }

    private byte ReadOneByte(string what)
    {
        int value;
        try
        {
            value = _source.ReadByte();
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                $"Failed to read {what} from the underlying stream.", ex);
        }

        if (value < 0)
            throw new BinaryFormatException($"{what} ended early.");

        return (byte)value;
    }
}
