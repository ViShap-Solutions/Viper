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

    /// <summary>
    /// <see langword="true"/> when the algorithm implements
    /// <see cref="Decompress(ReadOnlySpan{byte}, System.Buffers.IBufferWriter{byte}, int)"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>, which is correct for an implementation that only offers
    /// the span overload. The serializer then sizes the output buffer from the declared uncompressed
    /// length instead of from the bytes actually produced; that length is still bounded, but only by
    /// policy. Reporting <see langword="true"/> lets the serializer allocate as output arrives, so a
    /// payload that claims to expand far more than it does never causes the allocation it describes.
    /// </remarks>
    bool SupportsIncrementalDecompression => false;

    /// <summary>
    /// Decompresses <paramref name="source"/> into a writer that supplies space as output is
    /// produced, rather than into a buffer sized from the declared length.
    /// </summary>
    /// <remarks>
    /// The serializer calls this overload whenever <see cref="SupportsIncrementalDecompression"/> is
    /// <see langword="true"/>, and never otherwise, so the default implementation is unreachable for
    /// a correctly declared algorithm. An implementation must never produce more than
    /// <paramref name="maxOutputBytes"/> bytes, and must report input that would expand beyond it as
    /// <see cref="Exceptions.BinaryFormatException"/> rather than truncating silently.
    /// </remarks>
    /// <param name="source">The compressed bytes.</param>
    /// <param name="destination">Receives the decompressed bytes.</param>
    /// <param name="maxOutputBytes">The declared uncompressed length, which the output may not exceed.</param>
    /// <returns>The number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="Exceptions.BinaryFormatException">The compressed data is malformed or over-long.</exception>
    /// <exception cref="NotSupportedException">The algorithm does not implement incremental decompression.</exception>
    int Decompress(ReadOnlySpan<byte> source, System.Buffers.IBufferWriter<byte> destination, int maxOutputBytes) =>
        throw new NotSupportedException(
            $"'{GetType().Name}' does not implement incremental decompression.");
}
