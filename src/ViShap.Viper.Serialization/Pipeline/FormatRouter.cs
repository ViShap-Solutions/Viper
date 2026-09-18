namespace ViShap.Viper.Pipeline;

/// <summary>
/// Selects the wire format. Reading is self-describing: the magic number and version are peeked
/// without consuming the stream, and a stream without them is only treated as V0 when the caller
/// opted into that fallback.
/// </summary>
internal sealed class FormatRouter(IReadOnlyDictionary<int, IFormatPipeline> pipelines)
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
            else if (pipelines.ContainsKey(0))
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
