using System.IO.Compression;

namespace ViShap.Viper.Compression;

public sealed class Deflate(CompressionLevel level = CompressionLevel.Optimal) : ICompressionAlgorithm
{
    public CompressionAlgorithm Kind => CompressionAlgorithm.Deflate;
    public string? CustomName => null;
    public int GetMaxCompressedLength(int uncompressedLength) => uncompressedLength + (uncompressedLength / 3) + 128;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
            deflate.Write(source);

        byte[] compressed = output.ToArray();
        if (compressed.Length > destination.Length)
            throw new InvalidOperationException(
                $"Deflate output ({compressed.Length} bytes) exceeded the provided buffer ({destination.Length} bytes).");

        compressed.CopyTo(destination);
        return compressed.Length;
    }

    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        using var input = new MemoryStream(source.ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);

        try
        {
            int totalRead = 0;
            while (totalRead < destination.Length)
            {
                int read = deflate.Read(destination[totalRead..]);
                if (read == 0) break;
                totalRead += read;
            }
            return totalRead;
        }
        catch (InvalidDataException ex)
        {
            throw new BinaryFormatException(
                "Deflate decompression failed: the compressed payload is malformed.", ex);
        }
    }
}