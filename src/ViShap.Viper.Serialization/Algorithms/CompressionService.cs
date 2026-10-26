namespace ViShap.Viper.Compression;

/// <summary>
/// Runs a compression algorithm. It is internal on purpose: phase sizes are enforced by the buffer it
/// hands the algorithm, so the barrier cannot be replaced from outside, and an algorithm
/// implementation never has to know a limit. Every result is a pooled buffer owned by the caller.
/// </summary>
internal sealed class CompressionService(ICompressionAlgorithm algorithm)
{
    public CompressionAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;

    /// <summary>
    /// Compresses <paramref name="rawPayload"/> into a buffer that refuses to grow past
    /// <paramref name="maxCompressedBytes"/>. Not called for <see cref="CompressionAlgorithm.None"/>.
    /// </summary>
    public RentedBytes Compress(ReadOnlySpan<byte> rawPayload, long maxCompressedBytes)
    {
        using var buffer = CompressionBuffer.ForCompression(rawPayload.Length, maxCompressedBytes);

        algorithm.Compress(rawPayload, buffer);
        return buffer.Detach();
    }

    /// <summary>
    /// Produces exactly the declared number of bytes. The algorithm writes into a buffer that grows as
    /// output arrives and refuses to grow past the declared length, so the declared length bounds the
    /// result without being allocated up front; the pipeline has already checked it against
    /// <c>MaxPayloadBytes</c> and <c>MaxDecompressionRatio</c>. Output shorter than declared is
    /// refused here, whatever the algorithm reported. Not called for
    /// <see cref="CompressionAlgorithm.None"/>.
    /// </summary>
    public static RentedBytes Decompress(
        ICompressionAlgorithm algorithm,
        ReadOnlySpan<byte> compressedPayload,
        int uncompressedLength)
    {
        using var buffer = CompressionBuffer.ForDecompression(uncompressedLength);

        try
        {
            algorithm.Decompress(compressedPayload, buffer, uncompressedLength);
        }
        catch (InvalidDataException ex)
        {
            throw new BinaryFormatException(
                "Decompression failed because the compressed payload is malformed.", ex);
        }

        if (buffer.WrittenCount != uncompressedLength)
            throw new BinaryFormatException(
                $"Decompression produced {buffer.WrittenCount} bytes, expected {uncompressedLength}.");

        return buffer.Detach();
    }
}
