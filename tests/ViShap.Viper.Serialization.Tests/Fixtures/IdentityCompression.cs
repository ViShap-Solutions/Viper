using ViShap.Viper.Compression;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A custom compression algorithm that copies its input, so a test can put
/// <see cref="CompressionAlgorithm.Custom"/> and a custom name on the wire without depending on what
/// a real codec produces.
/// </summary>
internal sealed class IdentityCompression : ICompressionAlgorithm
{
    public const string RegisteredName = "identity";

    public CompressionAlgorithm Kind => CompressionAlgorithm.Custom;

    public string? CustomName => RegisteredName;

    public int GetMaxCompressedLength(int uncompressedLength) => uncompressedLength;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }

    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length != destination.Length)
            throw new BinaryFormatException(
                $"Identity compression received {source.Length} byte(s) for a {destination.Length} " +
                "byte output buffer.");

        source.CopyTo(destination);
        return destination.Length;
    }
}
