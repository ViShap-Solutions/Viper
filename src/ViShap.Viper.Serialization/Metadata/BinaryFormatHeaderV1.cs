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

    public void WriteTo(ValueWriter writer)
    {
        writer.WriteInt32(BinaryFormatConstants.Magic);
        writer.WriteInt32(Version);
        writer.WriteByte((byte)Compression);
        WriteOptionalString(writer, CustomCompressionName, nameof(CustomCompressionName));
        writer.WriteByte((byte)ChecksumAlgorithm);
        WriteOptionalString(writer, CustomChecksumName, nameof(CustomChecksumName));
        writer.WriteByte((byte)Encryption);
        WriteOptionalString(writer, CustomEncryptionName, nameof(CustomEncryptionName));
        WriteOptionalString(writer, KeyId, nameof(KeyId));
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

    public static BinaryFormatHeaderV1 ReadFrom(ValueReader reader)
    {
        int magic = reader.ReadInt32();
        if (magic != BinaryFormatConstants.Magic)
            throw new BinaryFormatException(
                "Not a recognized BinarySerializer stream (magic number mismatch).");

        int formatVersion = reader.ReadInt32();
        if (formatVersion != Version)
            throw new BinaryFormatNotSupportedException(
                $"Expected format version {Version}, but found {formatVersion}.");

        var compression = ReadEnum<CompressionAlgorithm>(reader, "compression");
        string? customCompression = ReadOptionalString(reader, nameof(CustomCompressionName));
        var checksumAlgorithm = ReadEnum<ChecksumAlgorithm>(reader, "checksum");
        string? customChecksum = ReadOptionalString(reader, nameof(CustomChecksumName));
        var encryption = ReadEnum<EncryptionAlgorithm>(reader, "encryption");
        string? customEncryption = ReadOptionalString(reader, nameof(CustomEncryptionName));
        string? keyId = ReadOptionalString(reader, nameof(KeyId));
        bool preserveReferences = reader.ReadBoolean();

        int uncompressedLength = reader.ReadInt32();
        int compressedLength = reader.ReadInt32();
        int onDiskLength = reader.ReadInt32();

        var limits = reader.Operation.Limits;
        CheckLength(uncompressedLength, limits.MaxPayloadBytes, nameof(UncompressedLength));
        CheckLength(compressedLength, limits.MaxCompressedBytes, nameof(CompressedLength));
        CheckLength(onDiskLength, limits.MaxEncryptedBytes, nameof(OnDiskLength));

        if (compression == CompressionAlgorithm.None && compressedLength != uncompressedLength)
            throw new BinaryFormatException(
                "CompressedLength must equal UncompressedLength when compression is None.");

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
        using var buffer = new MemoryStream(64);
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);

        writer.Write(Version);
        writer.Write((byte)Compression);
        writer.Write(CustomCompressionName ?? string.Empty);
        writer.Write((byte)ChecksumAlgorithm);
        writer.Write(CustomChecksumName ?? string.Empty);
        writer.Write((byte)Encryption);
        writer.Write(CustomEncryptionName ?? string.Empty);
        writer.Write(KeyId ?? string.Empty);
        writer.Write(PreserveReferences);
        writer.Write(UncompressedLength);
        writer.Write(CompressedLength);
        writer.Write((byte)Checksum.Length);
        writer.Write(Checksum);
        writer.Flush();

        return buffer.ToArray();
    }

    public BinaryHeaderInfo ToInfo() =>
        new(Version, Compression, CustomCompressionName,
            ChecksumAlgorithm, CustomChecksumName,
            Encryption, CustomEncryptionName, KeyId);

    private static void CheckLength(int value, long maximum, string what)
    {
        if (value < 0)
            throw new BinaryFormatException($"{what} {value} must be non-negative.");

        if (value > maximum)
            throw new BinaryLimitException(
                $"{what} {value} exceeds the configured maximum of {maximum}.");
    }

    private static TEnum ReadEnum<TEnum>(ValueReader reader, string what) where TEnum : struct, Enum
    {
        byte raw = reader.ReadByte();
        var value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
        if (!Enum.IsDefined(value))
            throw new BinaryFormatNotSupportedException($"Unknown {what} algorithm: {raw}.");

        return value;
    }

    private static void WriteOptionalString(ValueWriter writer, string? value, string what)
    {
        bool hasValue = !string.IsNullOrEmpty(value);
        writer.WriteBoolean(hasValue);
        if (hasValue)
            writer.WriteString(value!, BinaryFormatConstants.MaxHeaderStringBytes, what);
    }

    private static string? ReadOptionalString(ValueReader reader, string what) =>
        reader.ReadBoolean()
            ? reader.ReadString(BinaryFormatConstants.MaxHeaderStringBytes, what)
            : null;
}