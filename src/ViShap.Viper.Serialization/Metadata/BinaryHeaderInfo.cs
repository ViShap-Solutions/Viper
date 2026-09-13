namespace ViShap.Viper.Metadata;

public readonly record struct BinaryHeaderInfo(
    int FormatVersion,
    CompressionAlgorithm Compression,
    string? CustomCompressionName,
    ChecksumAlgorithm ChecksumAlgorithm,
    string? CustomChecksumName,
    EncryptionAlgorithm Encryption,
    string? CustomEncryptionName,
    string? KeyId);