using System.Buffers;
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

    /// <inheritdoc />
    public bool SupportsIncrementalDecompression => true;

    /// <inheritdoc />
    public int Decompress(
        ReadOnlySpan<byte> source,
        System.Buffers.IBufferWriter<byte> destination,
        int maxOutputBytes)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var decoder = new BrotliDecoder();
        var remaining = source;
        int total = 0;

        while (true)
        {
            int room = maxOutputBytes - total;
            if (room == 0)
            {
                // One byte of room beyond the declared length. Anything the decoder still wants to
                // produce means the payload declared less than it carries; anything other than a
                // clean end means the stream never terminated, however many bytes it yielded.
                Span<byte> probe = stackalloc byte[1];
                var probed = decoder.Decompress(remaining, probe, out _, out int extra);

                if (extra > 0 || probed == OperationStatus.DestinationTooSmall)
                    throw new BinaryFormatException(
                        "Brotli decompression produced more data than the declared uncompressed length.");

                if (probed != OperationStatus.Done)
                    throw new BinaryFormatException(
                        "Brotli decompression failed because the compressed payload is malformed.");

                return total;
            }

            var span = destination.GetSpan(Math.Min(ChunkSize, room));
            var status = decoder.Decompress(
                remaining, span[..Math.Min(span.Length, room)], out int consumed, out int produced);

            remaining = remaining[consumed..];
            if (produced > 0)
            {
                destination.Advance(produced);
                total += produced;
            }

            switch (status)
            {
                case OperationStatus.Done:
                    return total;

                case OperationStatus.InvalidData:
                    throw new BinaryFormatException(
                        "Brotli decompression failed because the compressed payload is malformed.");

                case OperationStatus.NeedMoreData:
                    throw new BinaryFormatException(
                        "Brotli decompression failed because the compressed payload is truncated.");

                default:
                    if (consumed == 0 && produced == 0)
                        throw new BinaryFormatException(
                            "Brotli decompression stopped making progress on the compressed payload.");
                    break;
            }
        }
    }

    /// <summary>
    /// Small enough to fit the probe the serializer starts with, so a stream that produces nothing
    /// never forces the output buffer to grow to the declared size.
    /// </summary>
    private const int ChunkSize = 16 * 1024;

    private int GetQuality() => level switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.SmallestSize => 11,
        _ => 4
    };
}
