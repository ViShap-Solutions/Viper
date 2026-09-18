namespace ViShap.Viper.Checksum;

/// <summary>
/// A checksum primitive. Implement it to plug an integrity check of your own into Viper.
/// </summary>
/// <remarks>
/// <para>
/// The checksum is computed over the raw, uncompressed payload and verified after decompression, so
/// it detects corruption introduced anywhere in the pipeline.
/// </para>
/// <para>
/// Implementations are pure mechanics and must be safe for concurrent use, or be supplied through a
/// factory that returns a fresh instance per resolution.
/// </para>
/// </remarks>
public interface IChecksumAlgorithm
{
    /// <summary>The identifier recorded in the payload header.</summary>
    ChecksumAlgorithm Kind { get; }

    /// <summary>
    /// The name recorded in the header when <see cref="Kind"/> is
    /// <see cref="ChecksumAlgorithm.Custom"/>; otherwise <see langword="null"/>.
    /// </summary>
    string? CustomName { get; }

    /// <summary>
    /// Size of the value <see cref="Compute"/> produces, in bytes. Must be constant for the instance
    /// and at most 255, the largest the V1 header can record.
    /// </summary>
    int HashSizeInBytes { get; }

    /// <summary>Computes the checksum of <paramref name="source"/>.</summary>
    /// <param name="source">The raw payload bytes.</param>
    /// <param name="destination">A buffer of exactly <see cref="HashSizeInBytes"/> bytes.</param>
    void Compute(ReadOnlySpan<byte> source, Span<byte> destination);
}
