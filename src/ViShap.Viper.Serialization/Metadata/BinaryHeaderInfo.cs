namespace ViShap.Viper.Metadata;

/// <summary>
/// The format metadata of a payload, as reported by <see cref="BinaryFormatInspector.Peek(Stream)"/>.
/// </summary>
/// <remarks>
/// This is what a payload says about itself before any of it is decoded: which format version wrote
/// it, whether it uses reference framing, which compression, checksum and encryption it needs to be
/// unwrapped, and how long its parts are. It never contains key material — <see cref="KeyId"/> only
/// names the key a reader should look up.
/// </remarks>
/// <param name="FormatVersion">Wire format version of the payload.</param>
/// <param name="PreserveReferences">Whether the payload carries reference frames, so shared and cyclic objects keep their identity.</param>
/// <param name="Compression">Compression applied to the payload; <see cref="CompressionAlgorithm.None"/> when there is none.</param>
/// <param name="CustomCompressionName">
/// Name of the custom compression algorithm when <paramref name="Compression"/> is
/// <see cref="CompressionAlgorithm.Custom"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="UncompressedLength">
/// The length of the payload once decompressed, as the header declares it; <see langword="null"/> when
/// the payload is not compressed.
/// </param>
/// <param name="ChecksumAlgorithm">Checksum stored with the payload; <see cref="ViShap.Viper.Checksum.ChecksumAlgorithm.None"/> when there is none.</param>
/// <param name="CustomChecksumName">
/// Name of the custom checksum algorithm when <paramref name="ChecksumAlgorithm"/> is
/// <see cref="ViShap.Viper.Checksum.ChecksumAlgorithm.Custom"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="Checksum">The checksum the header records over the raw payload; empty when there is none.</param>
/// <param name="Encryption">Encryption applied to the payload; <see cref="EncryptionAlgorithm.None"/> when there is none.</param>
/// <param name="CustomEncryptionName">
/// Name of the custom encryption algorithm when <paramref name="Encryption"/> is
/// <see cref="EncryptionAlgorithm.Custom"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="KeyId">
/// Identifies which key decrypts the payload, so a reader holding several can choose. Not secret, and
/// not key material.
/// </param>
/// <param name="HeaderLength">The bytes the header occupies at the start of the frame.</param>
/// <param name="OnDiskLength">The bytes that follow the header: the payload as stored, after compression and encryption.</param>
public readonly record struct BinaryHeaderInfo(
    int FormatVersion,
    bool PreserveReferences,
    CompressionAlgorithm Compression,
    string? CustomCompressionName,
    int? UncompressedLength,
    ChecksumAlgorithm ChecksumAlgorithm,
    string? CustomChecksumName,
    ReadOnlyMemory<byte> Checksum,
    EncryptionAlgorithm Encryption,
    string? CustomEncryptionName,
    string? KeyId,
    int HeaderLength,
    int OnDiskLength);
