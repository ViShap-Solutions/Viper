using System.IO.Hashing;

namespace ViShap.Viper.Checksum;

/// <summary>
/// XXH3 64-bit integrity check, 8 bytes.
/// </summary>
/// <remarks>
/// Detects accidental corruption with a far lower chance of a collision than
/// <see cref="Crc32Checksum"/>. It is not a message authentication code: anyone who can modify the
/// payload can recompute it. Use authenticated encryption when the threat is deliberate modification.
/// </remarks>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithChecksum(new XxHash3Checksum())
///     .Build();
/// </code>
/// </example>
public sealed class XxHash3Checksum : IChecksumAlgorithm
{
    /// <inheritdoc />
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.XxHash3;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int HashSizeInBytes => 8;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) =>
        XxHash3.Hash(source, destination);
}
