namespace ViShap.Viper.Compression;

public interface ICompressionAlgorithm
{
    CompressionAlgorithm Kind { get; }
    string? CustomName { get; }
    int GetMaxCompressedLength(int uncompressedLength);
    int Compress(ReadOnlySpan<byte> source, Span<byte> destination);
    int Decompress(ReadOnlySpan<byte> source, Span<byte> destination);
}