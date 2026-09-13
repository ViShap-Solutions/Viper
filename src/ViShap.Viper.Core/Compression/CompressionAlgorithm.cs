namespace ViShap.Viper.Compression;

public enum CompressionAlgorithm : byte
{
    None = 0,
    Deflate = 1,
    Brotli = 2,
    Custom = 255,
}