using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// The pass-through compression algorithm, used when compression is disabled. Payload bytes are
/// stored exactly as produced.
/// </summary>
public sealed class NoCompression : ICompressionAlgorithm
{
    /// <inheritdoc />
    public CompressionAlgorithm Kind => CompressionAlgorithm.None;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        destination.Write(source);
    }

    /// <inheritdoc />
    public void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Length != expectedLength)
            throw new BinaryFormatException(
                $"Stored payload length {source.Length} does not match the declared uncompressed " +
                $"length {expectedLength}.");

        destination.Write(source);
    }
}
