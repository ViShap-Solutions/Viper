using System.IO.Hashing;

namespace ViShap.Viper.Checksum;

/// <summary>
/// XXH3 128-bit integrity check, 16 bytes.
/// </summary>
/// <remarks>
/// For very large volumes of data, where the collision chance of a 64-bit checksum becomes
/// noticeable. It is not a message authentication code: anyone who can modify the payload can
/// recompute it. Use authenticated encryption when the threat is deliberate modification.
/// </remarks>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithChecksum(new XxHash128Checksum())
///     .Build();
/// </code>
/// </example>
public sealed class XxHash128Checksum : IChecksumAlgorithm
{
    /// <inheritdoc />
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.XxHash128;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int HashSizeInBytes => 16;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) =>
        XxHash128.Hash(source, destination);
}
