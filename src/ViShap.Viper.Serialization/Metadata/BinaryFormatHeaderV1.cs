using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace ViShap.Viper.Metadata;

/// <summary>
/// V1 envelope metadata. The header owns its own invariants and nothing else: it validates the
/// declared phase lengths, and it produces the canonical byte string that authenticated encryption
/// binds as associated data, so none of these fields can be altered without breaking the tag.
/// </summary>
internal readonly record struct BinaryFormatHeaderV1(
    CompressionAlgorithm Compression,
    string? CustomCompressionName,
    ChecksumAlgorithm ChecksumAlgorithm,
    string? CustomChecksumName,
    EncryptionAlgorithm Encryption,
    string? CustomEncryptionName,
    string? KeyId,
    bool PreserveReferences,
    int UncompressedLength,
    int CompressedLength,
    int OnDiskLength,
    byte[] Checksum)
{
    public const int Version = 1;

    /// <summary>
    /// The most bytes a V1 header can occupy: the fixed fields, four optional strings at their
    /// format ceiling with a two-byte length prefix each, and the longest checksum a length byte can
    /// declare. Reading a header never needs more than this many bytes of the source.
    /// </summary>
    public const int MaxLength =
        sizeof(int) + sizeof(int)
        + 3 * (sizeof(byte) + OptionalStringMaxLength)
        + OptionalStringMaxLength
        + sizeof(bool)
        + 3 * sizeof(int)
        + sizeof(byte) + byte.MaxValue;

    private const int OptionalStringMaxLength =
        sizeof(bool) + 2 + BinaryFormatConstants.MaxHeaderStringBytes;

    /// <summary>
    /// The header's fields as the measuring walk sees them: a run of fixed bytes (a positive length),
    /// an optional string (<see cref="OptionalString"/>), and the checksum, a length byte followed by
    /// that many bytes (<see cref="ChecksumBlock"/>).
    /// </summary>
    private static ReadOnlySpan<int> Layout =>
    [
        sizeof(int) + sizeof(int) + sizeof(byte), OptionalString,
        sizeof(byte), OptionalString,
        sizeof(byte), OptionalString,
        OptionalString,
        sizeof(bool) + 3 * sizeof(int),
        ChecksumBlock
    ];

    private const int OptionalString = -1;
    private const int ChecksumBlock = -2;

    /// <summary>
    /// Finds how long the header at the start of <paramref name="prefix"/> is, so that a source which
    /// must not be read past the frame can be asked for exactly the header's bytes.
    /// </summary>
    /// <param name="prefix">The first bytes of a frame, at least as far as they have arrived.</param>
    /// <param name="length">
    /// When the method returns <see langword="true"/>, the header's length — or the length of
    /// <paramref name="prefix"/> when its bytes are not a well-formed header, which
    /// <see cref="ReadFrom"/> then reports. When it returns <see langword="false"/>, the number of
    /// bytes, counted from the start, that must be present before the length can be found: never more
    /// than any header beginning with these bytes occupies, so asking the source for them never
    /// reaches past the frame.
    /// </param>
    /// <returns>Whether the length is known.</returns>
    public static bool TryMeasure(ReadOnlySpan<byte> prefix, out int length)
    {
        var layout = Layout;
        int position = 0;

        for (int field = 0; field < layout.Length; field++)
        {
            int size = layout[field];
            if (size > 0)
            {
                if (position + size > prefix.Length)
                    return Need(position + MinimumFrom(field), out length);

                position += size;
                continue;
            }

            if (position >= prefix.Length)
                return Need(position + MinimumFrom(field), out length);

            if (size == ChecksumBlock)
            {
                int checksumEnd = position + 1 + prefix[position];
                if (checksumEnd > prefix.Length)
                    return Need(checksumEnd, out length);

                position = checksumEnd;
                continue;
            }

            byte present = prefix[position++];
            if (present == 0)
                continue;

            if (present != 1)
                return Malformed(prefix, out length);

            int stringLength = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (position >= prefix.Length)
                    return Need(position + 1 + MinimumFrom(field + 1), out length);

                byte current = prefix[position++];
                stringLength |= (current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                    break;

                if (shift >= 14)
                    return Malformed(prefix, out length);
            }

            if (stringLength > BinaryFormatConstants.MaxHeaderStringBytes)
                return Malformed(prefix, out length);

            if (position + stringLength > prefix.Length)
                return Need(position + stringLength + MinimumFrom(field + 1), out length);

            position += stringLength;
        }

        length = position;
        return true;

        static bool Need(int required, out int length)
        {
            length = required;
            return false;
        }

        static bool Malformed(ReadOnlySpan<byte> prefix, out int length)
        {
            length = prefix.Length;
            return true;
        }
    }

    /// <summary>The fewest bytes the header's fields from <paramref name="field"/> on can occupy.</summary>
    private static int MinimumFrom(int field)
    {
        int minimum = 0;
        foreach (int size in Layout[field..])
            minimum += size > 0 ? size : 1;

        return minimum;
    }

    public void WriteTo(ref WireWriter writer)
    {
        writer.WriteInt32(BinaryFormatConstants.Magic);
        writer.WriteInt32(Version);
        writer.WriteByte((byte)Compression);
        WriteOptionalString(ref writer, CustomCompressionName, nameof(CustomCompressionName));
        writer.WriteByte((byte)ChecksumAlgorithm);
        WriteOptionalString(ref writer, CustomChecksumName, nameof(CustomChecksumName));
        writer.WriteByte((byte)Encryption);
        WriteOptionalString(ref writer, CustomEncryptionName, nameof(CustomEncryptionName));
        WriteOptionalString(ref writer, KeyId, nameof(KeyId));
        writer.WriteBoolean(PreserveReferences);
        writer.WriteInt32(UncompressedLength);
        writer.WriteInt32(CompressedLength);
        writer.WriteInt32(OnDiskLength);

        if (Checksum.Length > byte.MaxValue)
            throw new BinaryConfigurationException(
                $"Checksum length {Checksum.Length} cannot be represented by the V1 header.");

        writer.WriteByte((byte)Checksum.Length);
        writer.Write(Checksum);
    }

    public static BinaryFormatHeaderV1 ReadFrom(ref WireReader reader)
    {
        int magic = reader.ReadInt32();
        if (magic != BinaryFormatConstants.Magic)
            throw new BinaryFormatException(
                "Not a recognized BinarySerializer stream (magic number mismatch).");

        int formatVersion = reader.ReadInt32();
        if (formatVersion != Version)
            throw new BinaryFormatNotSupportedException(
                $"Expected format version {Version}, but found {formatVersion}.");

        var compression = ReadEnum<CompressionAlgorithm>(ref reader, "compression");
        string? customCompression = ReadOptionalString(ref reader, nameof(CustomCompressionName));
        var checksumAlgorithm = ReadEnum<ChecksumAlgorithm>(ref reader, "checksum");
        string? customChecksum = ReadOptionalString(ref reader, nameof(CustomChecksumName));
        var encryption = ReadEnum<EncryptionAlgorithm>(ref reader, "encryption");
        string? customEncryption = ReadOptionalString(ref reader, nameof(CustomEncryptionName));
        string? keyId = ReadOptionalString(ref reader, nameof(KeyId));
        bool preserveReferences = reader.ReadBoolean();

        int uncompressedLength = reader.ReadInt32();
        int compressedLength = reader.ReadInt32();
        int onDiskLength = reader.ReadInt32();

        var limits = reader.State.Limits;
        CheckLength(uncompressedLength, limits.MaxPayloadBytes, nameof(UncompressedLength));
        CheckLength(compressedLength, limits.MaxCompressedBytes, nameof(CompressedLength));
        CheckLength(onDiskLength, limits.MaxEncryptedBytes, nameof(OnDiskLength));

        if (compression == CompressionAlgorithm.None && compressedLength != uncompressedLength)
            throw new BinaryFormatException(
                "CompressedLength must equal UncompressedLength when compression is None.");

        CheckExpansion(compression, compressedLength, uncompressedLength, limits.MaxDecompressionRatio);

        if (encryption == EncryptionAlgorithm.None && onDiskLength != compressedLength)
            throw new BinaryFormatException(
                "OnDiskLength must equal CompressedLength when encryption is None.");

        byte checksumLength = reader.ReadByte();
        byte[] checksum = reader.ReadBytes(checksumLength, "Checksum");

        return new BinaryFormatHeaderV1(
            compression, customCompression,
            checksumAlgorithm, customChecksum,
            encryption, customEncryption,
            keyId, preserveReferences,
            uncompressedLength, compressedLength, onDiskLength,
            checksum);
    }

    /// <summary>
    /// The canonical metadata image bound to authenticated encryption. Every header field takes part
    /// except <see cref="OnDiskLength"/>, which is only known after encryption and is self-verifying:
    /// a wrong value either truncates the read or fails the authentication tag.
    /// </summary>
    public byte[] BuildAssociatedData()
    {
        var image = new byte[AssociatedDataLength];
        WriteAssociatedData(image);
        return image;
    }

    /// <summary>The length of the image <see cref="WriteAssociatedData"/> writes.</summary>
    public int AssociatedDataLength =>
        sizeof(int)
        + sizeof(byte) + ImageStringLength(CustomCompressionName)
        + sizeof(byte) + ImageStringLength(CustomChecksumName)
        + sizeof(byte) + ImageStringLength(CustomEncryptionName)
        + ImageStringLength(KeyId)
        + sizeof(bool)
        + 2 * sizeof(int)
        + sizeof(byte) + Checksum.Length;

    /// <summary>
    /// Writes the image of <see cref="BuildAssociatedData"/> into the first
    /// <see cref="AssociatedDataLength"/> bytes of <paramref name="destination"/>. Integers are
    /// little-endian, a boolean is one byte, and a string is its UTF-8 byte count as a 7-bit encoded
    /// integer followed by the bytes, an absent one written as empty.
    /// </summary>
    public void WriteAssociatedData(Span<byte> destination)
    {
        int position = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[position..], Version);
        position += sizeof(int);
        destination[position++] = (byte)Compression;
        position += WriteImageString(destination[position..], CustomCompressionName);
        destination[position++] = (byte)ChecksumAlgorithm;
        position += WriteImageString(destination[position..], CustomChecksumName);
        destination[position++] = (byte)Encryption;
        position += WriteImageString(destination[position..], CustomEncryptionName);
        position += WriteImageString(destination[position..], KeyId);
        destination[position++] = PreserveReferences ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[position..], UncompressedLength);
        position += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[position..], CompressedLength);
        position += sizeof(int);
        destination[position++] = (byte)Checksum.Length;
        Checksum.CopyTo(destination[position..]);
    }

    private static int ImageStringLength(string? value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value ?? string.Empty);
        int prefix = 1;
        for (uint remaining = (uint)byteCount; remaining >= 0x80; remaining >>= 7)
            prefix++;

        return prefix + byteCount;
    }

    private static int WriteImageString(Span<byte> destination, string? value)
    {
        value ??= string.Empty;
        int position = 0;
        uint remaining = (uint)Encoding.UTF8.GetByteCount(value);
        while (remaining >= 0x80)
        {
            destination[position++] = (byte)(remaining | 0x80);
            remaining >>= 7;
        }

        destination[position++] = (byte)remaining;
        return position + Encoding.UTF8.GetBytes(value, destination[position..]);
    }

    public BinaryHeaderInfo ToInfo() =>
        new(Version, Compression, CustomCompressionName,
            ChecksumAlgorithm, CustomChecksumName,
            Encryption, CustomEncryptionName, KeyId);

    /// <summary>
    /// Bounds the one declared length the wire cannot bound on its own. Every other phase length is
    /// backed by bytes that must physically arrive; the uncompressed length is not, because
    /// compression may legitimately expand. Relating it to the compressed length that does arrive is
    /// what keeps the decompression buffer proportional to the payload actually delivered.
    /// </summary>
    private static void CheckExpansion(
        CompressionAlgorithm compression,
        int compressedLength,
        int uncompressedLength,
        int maximumRatio)
    {
        if (compression == CompressionAlgorithm.None)
            return;

        long ceiling = (long)compressedLength * maximumRatio;
        if (uncompressedLength > ceiling)
            throw new BinaryLimitException(
                $"{nameof(UncompressedLength)} {uncompressedLength} exceeds {compressedLength} " +
                $"compressed byte(s) by more than the configured factor of {maximumRatio} " +
                $"({nameof(SerializationLimits.MaxDecompressionRatio)}).");
    }

    private static void CheckLength(int value, long maximum, string what)
    {
        if (value < 0)
            throw new BinaryFormatException($"{what} {value} must be non-negative.");

        if (value > maximum)
            throw new BinaryLimitException(
                $"{what} {value} exceeds the configured maximum of {maximum}.");
    }

    /// <summary>Reads an algorithm enum, whose underlying type is a byte, without boxing it.</summary>
    private static TEnum ReadEnum<TEnum>(ref WireReader reader, string what) where TEnum : unmanaged, Enum
    {
        byte raw = reader.ReadByte();
        var value = Unsafe.As<byte, TEnum>(ref raw);
        if (!Enum.IsDefined(value))
            throw new BinaryFormatNotSupportedException($"Unknown {what} algorithm: {raw}.");

        return value;
    }

    private static void WriteOptionalString(ref WireWriter writer, string? value, string what)
    {
        bool hasValue = !string.IsNullOrEmpty(value);
        writer.WriteBoolean(hasValue);
        if (hasValue)
            writer.WriteString(value!, BinaryFormatConstants.MaxHeaderStringBytes, what);
    }

    private static string? ReadOptionalString(ref WireReader reader, string what) =>
        reader.ReadBoolean()
            ? reader.ReadString(BinaryFormatConstants.MaxHeaderStringBytes, what)
            : null;
}