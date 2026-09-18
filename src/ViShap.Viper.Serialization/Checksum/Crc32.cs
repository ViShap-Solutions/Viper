namespace ViShap.Viper.Checksum;

/// <summary>
/// CRC-32 (IEEE 802.3) integrity check, 4 bytes.
/// </summary>
/// <remarks>
/// Detects accidental corruption — a bad disk, a truncated transfer — at negligible cost. It is not a
/// message authentication code: anyone who can modify the payload can recompute it. Use authenticated
/// encryption when the threat is deliberate modification.
/// </remarks>
public sealed class Crc32 : IChecksumAlgorithm
{
    /// <inheritdoc />
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.Crc32;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int HashSizeInBytes => 4;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) =>
        System.IO.Hashing.Crc32.Hash(source, destination);
}
