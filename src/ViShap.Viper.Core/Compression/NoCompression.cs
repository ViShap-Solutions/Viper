namespace ViShap.Viper.Compression;

public sealed class NoCompression : ICompressionAlgorithm
{
    public CompressionAlgorithm Kind => CompressionAlgorithm.None;
    public string? CustomName => null;
    public int GetMaxCompressedLength(int uncompressedLength) => uncompressedLength;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }

    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }
}