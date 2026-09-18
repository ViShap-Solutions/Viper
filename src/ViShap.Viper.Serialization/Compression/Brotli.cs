using System.IO.Compression;

namespace ViShap.Viper.Compression;

/// <summary>
/// Brotli compression (RFC 7932).
/// </summary>
/// <remarks>
/// Compresses better than <see cref="Deflate"/>, usually at a higher CPU cost. A good default when
/// payloads travel over a network or are stored for a long time; prefer <see cref="Deflate"/> when
/// throughput matters more than size.
/// </remarks>
/// <param name="level">
/// Compression effort. <see cref="CompressionLevel.Fastest"/> maps to Brotli quality 1,
/// <see cref="CompressionLevel.SmallestSize"/> to 11, <see cref="CompressionLevel.NoCompression"/> to
/// 0, and anything else to 4.
/// </param>
public sealed class Brotli(CompressionLevel level = CompressionLevel.Optimal) : ICompressionAlgorithm
{
    /// <inheritdoc />
    public CompressionAlgorithm Kind => CompressionAlgorithm.Brotli;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int GetMaxCompressedLength(int uncompressedLength) =>
        BrotliEncoder.GetMaxCompressedLength(uncompressedLength);

    /// <inheritdoc />
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (!BrotliEncoder.TryCompress(source, destination, out int bytesWritten, GetQuality(), window: 22))
            throw new InvalidOperationException(
                "Brotli compression failed because the destination buffer was insufficient.");

        return bytesWritten;
    }

    /// <inheritdoc />
    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (!BrotliDecoder.TryDecompress(source, destination, out int bytesWritten))
            throw new BinaryFormatException(
                "Brotli decompression failed because the compressed payload is malformed.");

        return bytesWritten;
    }

    private int GetQuality() => level switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.SmallestSize => 11,
        _ => 4
    };
}
