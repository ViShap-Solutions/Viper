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
    public int GetMaxCompressedLength(int uncompressedLength) => uncompressedLength;

    /// <inheritdoc />
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }

    /// <inheritdoc />
    public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }
}
