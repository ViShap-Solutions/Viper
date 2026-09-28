using System.Buffers;
using System.IO.Compression;

namespace ViShap.Viper.Compression;

/// <summary>
/// Raw DEFLATE compression (RFC 1951).
/// </summary>
/// <remarks>
/// Fast and widely compatible, with a lower ratio than <see cref="BrotliCompression"/>. A good default
/// when throughput matters more than size.
/// </remarks>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithCompression(new DeflateCompression(CompressionLevel.Fastest))
///     .Build();
/// </code>
/// </example>
/// <param name="level">Compression effort, passed through to <see cref="DeflateStream"/>.</param>
public sealed class DeflateCompression(CompressionLevel level = CompressionLevel.Optimal) : ICompressionAlgorithm
{
    /// <summary>
    /// The most output asked of the decoder at once, so a stream that produces little never makes the
    /// destination grow further than it has to.
    /// </summary>
    private const int ChunkSize = 16 * 1024;

    /// <inheritdoc />
    public CompressionAlgorithm Kind => CompressionAlgorithm.Deflate;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var output = new BufferWriterStream(destination);
        using var deflate = new DeflateStream(output, level, leaveOpen: true);
        deflate.Write(source);
    }

    /// <inheritdoc />
    public void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);

        byte[] input = RentedBytes.RentArray(source.Length);
        try
        {
            source.CopyTo(input);

            using var deflate = new DeflateStream(
                new ArrayReadStream(input, source.Length), CompressionMode.Decompress);

            int total = 0;
            while (total < expectedLength)
            {
                int room = Math.Min(ChunkSize, expectedLength - total);
                var span = destination.GetSpan(room);

                int read = deflate.Read(span[..Math.Min(span.Length, room)]);
                if (read == 0)
                    throw new BinaryFormatException(
                        $"Deflate decompression produced {total} bytes, expected {expectedLength}.");

                destination.Advance(read);
                total += read;
            }

            if (deflate.ReadByte() != -1)
                throw new BinaryFormatException(
                    "Deflate decompression produced more data than the declared uncompressed length.");
        }
        catch (InvalidDataException ex)
        {
            throw new BinaryFormatException(
                "Deflate decompression failed because the compressed payload is malformed.", ex);
        }
        finally
        {
            RentedBytes.ReturnArray(input);
        }
    }
}
