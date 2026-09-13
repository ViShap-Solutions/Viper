using System.Buffers;

namespace ViShap.Viper.Compression;

public sealed class Compressor(ICompressionAlgorithm algorithm) : ICompressor
{
    private readonly ICompressionAlgorithm _defaultAlgorithm = algorithm;

    private Compressor() : this(new NoCompression()) { }
    public static Compressor None { get; } = new();
    
    public CompressionAlgorithm DefaultKind => _defaultAlgorithm.Kind;
    public string? DefaultCustomName => _defaultAlgorithm.CustomName;

    public byte[] Compress(byte[] rawPayload)
    {
        if (_defaultAlgorithm.Kind == CompressionAlgorithm.None) return rawPayload;

        int maxLength = _defaultAlgorithm.GetMaxCompressedLength(rawPayload.Length);
        byte[] rented = ArrayPool<byte>.Shared.Rent(maxLength);
        try
        {
            int written = _defaultAlgorithm.Compress(rawPayload, rented);
            return rented.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public byte[] Decompress(CompressionAlgorithm kind, string? customName, byte[] compressedPayload, int uncompressedLength)
    {
        if (kind == CompressionAlgorithm.None) return compressedPayload;

        var algorithm = CompressionAlgorithmRegistry.Resolve(kind, customName);
        var result = new byte[uncompressedLength];
        int written = algorithm.Decompress(compressedPayload, result);

        if (written != uncompressedLength)
            throw new BinaryFormatException($"Decompression produced {written} bytes, expected {uncompressedLength}.");

        return result;
    }
}