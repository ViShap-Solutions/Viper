namespace ViShap.Viper.Metadata;

/// <summary>
/// The format metadata of a payload, as reported by <see cref="BinaryFormatInspector.Peek(Stream)"/>.
/// </summary>
/// <remarks>
/// This is what a payload says about itself before any of it is decoded: which format version wrote
/// it, and which compression, checksum and encryption it needs to be unwrapped. It never contains key
/// material — <see cref="KeyId"/> only names the key a reader should look up.
/// </remarks>
/// <param name="FormatVersion">Wire format version of the payload.</param>
/// <param name="Compression">Compression applied to the payload.</param>
/// <param name="CustomCompressionName">
/// Name of the custom compression algorithm when <paramref name="Compression"/> is
/// <see cref="CompressionAlgorithm.Custom"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="ChecksumAlgorithm">Checksum stored with the payload.</param>
/// <param name="CustomChecksumName">
/// Name of the custom checksum algorithm when <paramref name="ChecksumAlgorithm"/> is
/// <see cref="Checksum.ChecksumAlgorithm.Custom"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="Encryption">Encryption applied to the payload.</param>
/// <param name="CustomEncryptionName">
/// Name of the custom encryption algorithm when <paramref name="Encryption"/> is
/// <see cref="EncryptionAlgorithm.Custom"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="KeyId">
/// Identifies which key decrypts the payload, so a reader holding several can choose. Not secret, and
/// not key material.
/// </param>
public readonly record struct BinaryHeaderInfo(
    int FormatVersion,
    CompressionAlgorithm Compression,
    string? CustomCompressionName,
    ChecksumAlgorithm ChecksumAlgorithm,
    string? CustomChecksumName,
    EncryptionAlgorithm Encryption,
    string? CustomEncryptionName,
    string? KeyId);
