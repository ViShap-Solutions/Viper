namespace ViShap.Viper.Compression;

/// <summary>
/// A compression primitive. Implement it to plug an algorithm of your own into Viper.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are pure mechanics: they transform spans and nothing else. Resource limits,
/// framing and buffer lifetime belong to the serializer, which calls the algorithm inside those
/// checks, so an implementation can neither weaken a limit nor be asked to enforce one.
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

    /// <summary>
    /// The largest output <see cref="Compress"/> can produce for an input of
    /// <paramref name="uncompressedLength"/> bytes. May overestimate; must never underestimate.
    /// </summary>
    /// <param name="uncompressedLength">Length of the input, in bytes.</param>
    /// <returns>An upper bound on the compressed size, in bytes.</returns>
    int GetMaxCompressedLength(int uncompressedLength);

    /// <summary>Compresses <paramref name="source"/> into <paramref name="destination"/>.</summary>
    /// <param name="source">The bytes to compress.</param>
    /// <param name="destination">
    /// The output buffer. It may be smaller than <see cref="GetMaxCompressedLength"/> suggests when a
    /// configured limit caps it; in that case failing is correct and the serializer reports the limit.
    /// </param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    int Compress(ReadOnlySpan<byte> source, Span<byte> destination);

    /// <summary>Decompresses <paramref name="source"/> into <paramref name="destination"/>.</summary>
    /// <param name="source">The compressed bytes.</param>
    /// <param name="destination">
    /// A buffer of exactly the declared uncompressed size. An implementation must fill it completely
    /// and must reject input that would expand beyond it, so that a payload cannot declare a size
    /// that hides part of its own content.
    /// </param>
    /// <returns>The number of bytes written, which must equal the length of <paramref name="destination"/>.</returns>
    /// <exception cref="Exceptions.BinaryFormatException">The compressed data is malformed or over-long.</exception>
    int Decompress(ReadOnlySpan<byte> source, Span<byte> destination);
}
