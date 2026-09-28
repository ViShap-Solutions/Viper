using System.Buffers;
using System.IO.Compression;

namespace ViShap.Viper.Compression;

/// <summary>
/// Brotli compression (RFC 7932).
/// </summary>
/// <remarks>
/// Compresses better than <see cref="DeflateCompression"/>, usually at a higher CPU cost. A good
/// default when payloads travel over a network or are stored for a long time; prefer
/// <see cref="DeflateCompression"/> when throughput matters more than size.
/// </remarks>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithCompression(new BrotliCompression())
///     .Build();
/// </code>
/// </example>
/// <param name="level">
/// Compression effort. <see cref="CompressionLevel.Fastest"/> maps to Brotli quality 1,
/// <see cref="CompressionLevel.SmallestSize"/> to 11, <see cref="CompressionLevel.NoCompression"/> to
/// 0, and anything else to 4.
/// </param>
public sealed class BrotliCompression(CompressionLevel level = CompressionLevel.Optimal) : ICompressionAlgorithm
{
    /// <summary>The base-2 logarithm of the sliding window, 4 MiB.</summary>
    private const int Window = 22;

    /// <summary>
    /// The most output asked of the codec at once, so a stream that produces little never makes the
    /// destination grow further than it has to.
    /// </summary>
    private const int ChunkSize = 16 * 1024;

    /// <inheritdoc />
    public CompressionAlgorithm Kind => CompressionAlgorithm.Brotli;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var encoder = new BrotliEncoder(GetQuality(), Window);
        var remaining = source;

        while (true)
        {
            var span = destination.GetSpan(ChunkSize);
            var status = encoder.Compress(
                remaining, span, out int consumed, out int written, isFinalBlock: true);

            remaining = remaining[consumed..];
            destination.Advance(written);

            switch (status)
            {
                case OperationStatus.Done:
                    return;

                case OperationStatus.DestinationTooSmall:
                    break;

                default:
                    throw new InvalidOperationException($"Brotli compression stopped with status {status}.");
            }
        }
    }

    /// <inheritdoc />
    public void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);

        using var decoder = new BrotliDecoder();
        var remaining = source;
        int total = 0;

        while (true)
        {
            int room = expectedLength - total;
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

                return;
            }

            int chunk = Math.Min(ChunkSize, room);
            var span = destination.GetSpan(chunk);
            var status = decoder.Decompress(
                remaining, span[..Math.Min(span.Length, chunk)], out int consumed, out int produced);

            remaining = remaining[consumed..];
            if (produced > 0)
            {
                destination.Advance(produced);
                total += produced;
            }

            switch (status)
            {
                case OperationStatus.Done:
                    if (total != expectedLength)
                        throw new BinaryFormatException(
                            $"Brotli decompression produced {total} bytes, expected {expectedLength}.");
                    return;

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

    private int GetQuality() => level switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.SmallestSize => 11,
        _ => 4
    };
}
