namespace ViShap.Viper.Metadata;

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

    public void WriteTo(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(BinaryFormatConstants.Magic);
        writer.Write(Version);
        
        writer.Write((byte)Compression);
        writer.WriteOptionalString(CustomCompressionName);

        writer.Write((byte)ChecksumAlgorithm);
        writer.WriteOptionalString(CustomChecksumName);

        writer.Write((byte)Encryption);
        writer.WriteOptionalString(CustomEncryptionName);

        writer.WriteOptionalString(KeyId);
        
        writer.Write(PreserveReferences);

        writer.Write(UncompressedLength);
        writer.Write(CompressedLength);
        writer.Write(OnDiskLength);

        writer.Write((byte)Checksum.Length);
        if (Checksum.Length > 0) writer.Write(Checksum);
    }

    public static BinaryFormatHeaderV1 ReadFrom(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int magic = reader.ReadInt32();
        if (magic != BinaryFormatConstants.Magic)
            throw new BinaryFormatException("Not a recognized BinarySerializer stream (magic number mismatch).");

        int formatVersion = reader.ReadInt32();
        if (formatVersion != Version)
            throw new BinaryFormatException($"Expected format version {Version}, but found {formatVersion}.");

        var compression = (CompressionAlgorithm)reader.ReadByte();
        if (!Enum.IsDefined(compression))
            throw new BinaryFormatNotSupportedException($"Unknown compression algorithm: {compression}.");
        var customCompression = reader.ReadOptionalString();

        var checksumAlgorithm = (ChecksumAlgorithm)reader.ReadByte();
        if (!Enum.IsDefined(checksumAlgorithm))
            throw new BinaryFormatNotSupportedException($"Unknown checksum algorithm: {checksumAlgorithm}.");
        var customChecksum = reader.ReadOptionalString();

        var encryption = (EncryptionAlgorithm)reader.ReadByte();
        if (!Enum.IsDefined(encryption))
            throw new BinaryFormatNotSupportedException($"Unknown encryption algorithm: {encryption}.");
        var customEncryption = reader.ReadOptionalString();

        var keyId = reader.ReadOptionalString();
        
        bool preserveReferences = reader.ReadBoolean();

        int uncompressedLength = reader.ReadInt32();
        int compressedLength = reader.ReadInt32();
        int onDiskLength = reader.ReadInt32();

        if (uncompressedLength < 0 || compressedLength < 0 || onDiskLength < 0)
            throw new BinaryFormatException("V1 lengths must be non-negative.");

        if (encryption == EncryptionAlgorithm.None && onDiskLength != compressedLength)
            throw new BinaryFormatException("OnDiskLength must equal CompressedLength when encryption is None.");

        byte checksumLength = reader.ReadByte();
        byte[] checksum = reader.ReadBytes(checksumLength);
        if (checksum.Length != checksumLength)
            throw new BinaryFormatException($"Checksum bytes ended early. Expected {checksumLength}, got {checksum.Length}.");

        return new BinaryFormatHeaderV1(
            compression, customCompression,
            checksumAlgorithm, customChecksum,
            encryption, customEncryption,
            keyId,
            preserveReferences,
            uncompressedLength, compressedLength, onDiskLength,
            checksum);
    }
}