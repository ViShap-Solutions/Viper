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

    /// <summary>How many times this instance compressed, so a test can prove what ran before it.</summary>
    public int CompressCalls { get; private set; }

    /// <summary>How many times this instance decompressed.</summary>
    public int DecompressCalls { get; private set; }

    public int GetMaxCompressedLength(int uncompressedLength) => uncompressedLength;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        CompressCalls++;
        source.CopyTo(destination);
        return source.Length;
    }

    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        DecompressCalls++;
        if (source.Length != destination.Length)
            throw new BinaryFormatException(
                $"Identity compression received {source.Length} byte(s) for a {destination.Length} " +
                "byte output buffer.");

        source.CopyTo(destination);
        return destination.Length;
    }
}
