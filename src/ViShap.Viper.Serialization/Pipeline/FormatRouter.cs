namespace ViShap.Viper.Pipeline;

/// <summary>
/// Selects the wire format. A V1 payload describes itself: the magic number and version are peeked
/// without consuming the stream. A V0 payload does not, so a stream without them is read as V0 only
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

        int version;
        try
        {
            if (BinaryHeaderPeek.TryPeekMagicAndVersion(source, out int detected))
                version = detected;
            else if (allowHeaderlessFallback && pipelines.ContainsKey(0))
                version = 0;
            else
                throw new BinaryFormatException(
                    "Not a recognized BinarySerializer stream (magic number mismatch and the V0 " +
                    "fallback is disabled).");
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to inspect the source stream while detecting the binary format.", ex);
        }

        return pipelines.TryGetValue(version, out var pipeline)
            ? pipeline
            : throw new BinaryFormatNotSupportedException(
                $"No pipeline registered for format version {version}.");
    }
}
