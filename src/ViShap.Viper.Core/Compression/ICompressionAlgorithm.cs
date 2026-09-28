using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// A compression primitive. Implement it to plug an algorithm of your own into Viper.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are pure mechanics: they transform bytes and nothing else. Resource limits,
/// framing and buffer lifetime belong to the serializer, which calls the algorithm inside those
/// checks, so an implementation can neither weaken a limit nor be asked to enforce one. The writer it
/// receives refuses space beyond what the serializer allows.
/// </para>
/// <para>
/// An implementation must be safe for concurrent use, or must be supplied through a factory that
/// returns a fresh instance per resolution.
/// </para>
/// <para>
/// To use a custom algorithm, set <see cref="Kind"/> to <see cref="CompressionAlgorithm.Custom"/>,
/// give it a stable <see cref="CustomName"/>, and register the same name on the reading side with
/// <c>RegisterCustomCompression</c>. The name is what a payload carries.
/// </para>
/// </remarks>
public interface ICompressionAlgorithm
{
    /// <summary>The identifier recorded in the payload header.</summary>
    CompressionAlgorithm Kind { get; }

    /// <summary>
    /// The name recorded in the header when <see cref="Kind"/> is
    /// <see cref="CompressionAlgorithm.Custom"/>; otherwise <see langword="null"/>.
    /// </summary>
    string? CustomName { get; }

    /// <summary>Compresses <paramref name="source"/> into <paramref name="destination"/>.</summary>
    /// <param name="source">The bytes to compress.</param>
    /// <param name="destination">
    /// Receives the compressed bytes. Ask it for space as output is produced and advance it by what
    /// was written; it refuses space beyond the configured maximum, and the serializer reports that
    /// as a limit.
    /// </param>
    void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination);

    /// <summary>
    /// Decompresses <paramref name="source"/> into <paramref name="destination"/>, producing exactly
    /// <paramref name="expectedLength"/> bytes.
    /// </summary>
    /// <param name="source">The compressed bytes.</param>
    /// <param name="destination">
    /// Receives the decompressed bytes. It grows as output arrives, so a payload that declares more
    /// than it carries never causes the allocation it describes, and it refuses space beyond
    /// <paramref name="expectedLength"/>.
    /// </param>
    /// <param name="expectedLength">The declared uncompressed length.</param>
    /// <exception cref="Exceptions.BinaryFormatException">
    /// The compressed data is malformed, does not terminate, or decompresses to more or fewer than
    /// <paramref name="expectedLength"/> bytes.
    /// </exception>
    void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength);
}
