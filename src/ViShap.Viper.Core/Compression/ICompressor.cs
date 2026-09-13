namespace ViShap.Viper.Compression;

public interface ICompressor
{
    CompressionAlgorithm DefaultKind { get; }
    string? DefaultCustomName { get; }
    byte[] Compress(byte[] rawPayload);
    byte[] Decompress(CompressionAlgorithm kind, string? customName, byte[] compressedPayload, int uncompressedLength);
}