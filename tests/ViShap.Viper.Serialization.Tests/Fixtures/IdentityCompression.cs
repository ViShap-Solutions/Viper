using System.Buffers;
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

    private readonly string _name;

    /// <summary>A pass-through compression registered under <paramref name="name"/>.</summary>
    public IdentityCompression(string name = RegisteredName) => _name = name;

    public string? CustomName => _name;

    /// <summary>How many times this instance compressed, so a test can prove what ran before it.</summary>
    public int CompressCalls { get; private set; }

    /// <summary>How many times this instance decompressed.</summary>
    public int DecompressCalls { get; private set; }

    public void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination)
    {
        CompressCalls++;
        destination.Write(source);
    }

    public void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength)
    {
        DecompressCalls++;
        if (source.Length != expectedLength)
            throw new BinaryFormatException(
                $"Identity compression received {source.Length} byte(s) for a declared {expectedLength} " +
                "byte output.");

        destination.Write(source);
    }
}
