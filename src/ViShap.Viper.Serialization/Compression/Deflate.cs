using System.IO.Compression;

namespace ViShap.Viper.Compression;

/// <summary>
/// Raw DEFLATE compression (RFC 1951).
/// </summary>
/// <remarks>
/// Fast and widely compatible, with a lower ratio than <see cref="Brotli"/>. A good default when
/// throughput matters more than size.
/// </remarks>
/// <param name="level">Compression effort, passed through to <see cref="DeflateStream"/>.</param>
public sealed class Deflate(CompressionLevel level = CompressionLevel.Optimal) : ICompressionAlgorithm
{
    /// <inheritdoc />
    public CompressionAlgorithm Kind => CompressionAlgorithm.Deflate;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int GetMaxCompressedLength(int uncompressedLength) =>
        uncompressedLength + (uncompressedLength / 3) + 128;

    /// <inheritdoc />
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
            deflate.Write(source);

        if (output.Length > destination.Length)
            throw new InvalidOperationException(
                $"Deflate output ({output.Length} bytes) exceeded the provided buffer " +
                $"({destination.Length} bytes).");

        output.GetBuffer().AsSpan(0, (int)output.Length).CopyTo(destination);
        return (int)output.Length;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Output shorter than <paramref name="destination"/> leaves the caller's length check to fail;
    /// output longer than it is rejected here, so a payload cannot declare a size that hides part of
    /// its own content.
    /// </remarks>
    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        try
        {
            using var input = new MemoryStream(source.ToArray(), writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);

            int totalRead = 0;
            while (totalRead < destination.Length)
            {
                int read = deflate.Read(destination[totalRead..]);
                if (read == 0)
                    break;

                totalRead += read;
            }

            if (totalRead == destination.Length && deflate.ReadByte() != -1)
                throw new BinaryFormatException(
                    "Deflate decompression produced more data than the declared uncompressed length.");

            return totalRead;
        }
        catch (InvalidDataException ex)
        {
            throw new BinaryFormatException(
                "Deflate decompression failed because the compressed payload is malformed.", ex);
        }
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

        try
        {
            using var input = new MemoryStream(source.ToArray(), writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);

            int total = 0;
            while (total < maxOutputBytes)
            {
                int room = maxOutputBytes - total;
                var span = destination.GetSpan(Math.Min(ChunkSize, room));

                int read = deflate.Read(span[..Math.Min(span.Length, room)]);
                if (read == 0)
                    break;

                destination.Advance(read);
                total += read;
            }

            if (total == maxOutputBytes && deflate.ReadByte() != -1)
                throw new BinaryFormatException(
                    "Deflate decompression produced more data than the declared uncompressed length.");

            return total;
        }
        catch (InvalidDataException ex)
        {
            throw new BinaryFormatException(
                "Deflate decompression failed because the compressed payload is malformed.", ex);
        }
    }

    /// <summary>
    /// Small enough to fit the probe the serializer starts with, so a stream that produces nothing
    /// never forces the output buffer to grow to the declared size.
    /// </summary>
    private const int ChunkSize = 16 * 1024;
}
