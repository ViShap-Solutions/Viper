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

    public void WriteTo(BinaryWriter writer, SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        writer.Write(BinaryFormatConstants.Magic);
        writer.Write(Version);
        writer.Write((byte)Compression);
        writer.WriteOptionalString(CustomCompressionName, actualLimits, "Custom compression name");
        writer.Write((byte)ChecksumAlgorithm);
        writer.WriteOptionalString(CustomChecksumName, actualLimits, "Custom checksum name");
        writer.Write((byte)Encryption);
        writer.WriteOptionalString(CustomEncryptionName, actualLimits, "Custom encryption name");
        writer.WriteOptionalString(KeyId, actualLimits, "Key ID");
        writer.Write(PreserveReferences);

        DeserializationGuard.ValidateLength(
            UncompressedLength,
            actualLimits.MaxPayloadBytes,
            "UncompressedLength");
        DeserializationGuard.ValidateLength(
            CompressedLength,
            actualLimits.MaxCompressedBytes,
            "CompressedLength");
        DeserializationGuard.ValidateLength(
            OnDiskLength,
            actualLimits.MaxEncryptedBytes,
            "OnDiskLength");

        writer.Write(UncompressedLength);
        writer.Write(CompressedLength);
        writer.Write(OnDiskLength);

        if (Checksum.Length > byte.MaxValue)
            throw new BinaryConfigurationException(
                $"Checksum length {Checksum.Length} cannot be represented by the V1 header.");

        writer.Write((byte)Checksum.Length);
        if (Checksum.Length > 0)
            writer.Write(Checksum);
    }

    public static BinaryFormatHeaderV1 ReadFrom(
        BinaryReader reader,
        SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        try
        {
            int magic = reader.ReadInt32();
            if (magic != BinaryFormatConstants.Magic)
                throw new BinaryFormatException(
                    "Not a recognized BinarySerializer stream (magic number mismatch).");

            int formatVersion = reader.ReadInt32();
            if (formatVersion != Version)
                throw new BinaryFormatNotSupportedException(
                    $"Expected format version {Version}, but found {formatVersion}.");

            var compression = (CompressionAlgorithm)reader.ReadByte();
            if (!Enum.IsDefined(compression))
                throw new BinaryFormatNotSupportedException(
                    $"Unknown compression algorithm: {compression}.");

            var customCompression = reader.ReadOptionalString(
                actualLimits,
                "Custom compression name");

            var checksumAlgorithm = (ChecksumAlgorithm)reader.ReadByte();
            if (!Enum.IsDefined(checksumAlgorithm))
                throw new BinaryFormatNotSupportedException(
                    $"Unknown checksum algorithm: {checksumAlgorithm}.");

            var customChecksum = reader.ReadOptionalString(
                actualLimits,
                "Custom checksum name");

            var encryption = (EncryptionAlgorithm)reader.ReadByte();
            if (!Enum.IsDefined(encryption))
                throw new BinaryFormatNotSupportedException(
                    $"Unknown encryption algorithm: {encryption}.");

            var customEncryption = reader.ReadOptionalString(
                actualLimits,
                "Custom encryption name");

            var keyId = reader.ReadOptionalString(actualLimits, "Key ID");
            bool preserveReferences = reader.ReadBoolean();

            int uncompressedLength = reader.ReadInt32();
            int compressedLength = reader.ReadInt32();
            int onDiskLength = reader.ReadInt32();

            DeserializationGuard.ValidateLength(
                uncompressedLength,
                actualLimits.MaxPayloadBytes,
                "UncompressedLength");
            
            DeserializationGuard.ValidateLength(
                compressedLength,
                actualLimits.MaxCompressedBytes,
                "CompressedLength");
            
            DeserializationGuard.ValidateLength(
                onDiskLength,
                actualLimits.MaxEncryptedBytes,
                "OnDiskLength");

            if (compression == CompressionAlgorithm.None &&
                compressedLength != uncompressedLength)
            {
                throw new BinaryFormatException(
                    "CompressedLength must equal UncompressedLength when compression is None.");
            }

            if (encryption == EncryptionAlgorithm.None &&
                onDiskLength != compressedLength)
            {
                throw new BinaryFormatException(
                    "OnDiskLength must equal CompressedLength when encryption is None.");
            }

            byte checksumLength = reader.ReadByte();
            byte[] checksum = DeserializationGuard.ReadExactly(
                reader.BaseStream,
                checksumLength,
                "Checksum");

            return new BinaryFormatHeaderV1(
                compression,
                customCompression,
                checksumAlgorithm,
                customChecksum,
                encryption,
                customEncryption,
                keyId,
                preserveReferences,
                uncompressedLength,
                compressedLength,
                onDiskLength,
                checksum);
        }
        catch (EndOfStreamException ex)
        {
            throw new BinaryFormatException("V1 header is truncated.", ex);
        }
    }
}