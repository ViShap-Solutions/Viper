namespace ViShap.Viper.Pipeline;

/// <summary>
/// Selects the wire format. A V1 payload describes itself: the magic number and version are peeked
/// without consuming the source. A V0 payload does not, so a source without them is read as V0 only
/// when the caller opted in, and is otherwise rejected rather than guessed at.
/// </summary>
internal sealed class FormatRouter(
    IReadOnlyDictionary<int, IFormatPipeline> pipelines,
    bool allowHeaderlessFallback)
{
    public IFormatPipeline ForWriting(int version) =>
        pipelines.TryGetValue(version, out var pipeline)
            ? pipeline
            : throw new BinaryFormatNotSupportedException(
                $"No pipeline registered for format version {version}.");

    public IFormatPipeline ForReading(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanSeek)
            throw new NotSupportedException(
                "Reading needs a seekable stream so the format version can be detected.");

        bool detected;
        int version;
        try
        {
            detected = BinaryHeaderPeek.TryPeekMagicAndVersion(source, out version);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to inspect the source stream while detecting the binary format.", ex);
        }

        return Select(detected, version);
    }

    public IFormatPipeline ForReading(ReadOnlySpan<byte> source) =>
        Select(BinaryHeaderPeek.TryReadMagicAndVersion(source, out int version), version);

    private IFormatPipeline Select(bool detected, int version)
    {
        if (!detected)
        {
            if (!allowHeaderlessFallback || !pipelines.ContainsKey(0))
                throw new BinaryFormatException(
                    "Not a recognized BinarySerializer stream (magic number mismatch and the V0 " +
                    "fallback is disabled).");

            version = 0;
        }

        return pipelines.TryGetValue(version, out var pipeline)
            ? pipeline
            : throw new BinaryFormatNotSupportedException(
                $"No pipeline registered for format version {version}.");
    }
}
