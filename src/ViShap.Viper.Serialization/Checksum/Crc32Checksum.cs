using System.IO.Hashing;

namespace ViShap.Viper.Checksum;

/// <summary>
/// CRC-32 (IEEE 802.3) integrity check, 4 bytes.
/// </summary>
/// <remarks>
/// Detects accidental corruption — a bad disk, a truncated transfer — at negligible cost. It is not a
/// message authentication code: anyone who can modify the payload can recompute it. Use authenticated
/// encryption when the threat is deliberate modification.
/// </remarks>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithChecksum(new Crc32Checksum())
///     .Build();
/// </code>
/// </example>
public sealed class Crc32Checksum : IChecksumAlgorithm
{
    /// <inheritdoc />
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.Crc32;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int HashSizeInBytes => 4;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) =>
        Crc32.Hash(source, destination);
}
