using System.IO.Compression;

namespace ViShap.Viper.Compression;

public sealed class Brotli(CompressionLevel level = CompressionLevel.Optimal) : ICompressionAlgorithm
{
    public CompressionAlgorithm Kind => CompressionAlgorithm.Brotli;
    public string? CustomName => null;
    public int GetMaxCompressedLength(int uncompressedLength) => BrotliEncoder.GetMaxCompressedLength(uncompressedLength);

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (!BrotliEncoder.TryCompress(source, destination, out int bytesWritten, GetQuality(), window: 22))
            throw new IOException("Brotli compression failed — destination buffer may be too small.");
        return bytesWritten;
    }

    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (!BrotliDecoder.TryDecompress(source, destination, out int bytesWritten))
            throw new IOException("Brotli decompression failed.");
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