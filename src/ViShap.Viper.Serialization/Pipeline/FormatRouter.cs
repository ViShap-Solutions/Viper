using System.Buffers.Binary;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// Selects the wire format. A V1 payload describes itself: its first bytes are the magic number and
/// the version, decoded from the bytes the source has already delivered, so nothing is read twice and
/// no source is asked to rewind. A V0 payload does not describe itself, so bytes without the magic are
/// read as V0 only when the caller opted in, and are otherwise rejected rather than guessed at.
/// </summary>
internal sealed class FormatRouter(
    IReadOnlyDictionary<int, IFormatPipeline> pipelines,
    bool allowHeaderlessFallback)
{
    /// <summary>The magic number and the version: the bytes that identify a versioned frame.</summary>
    public const int PrefixLength = 2 * sizeof(int);

    public IFormatPipeline ForWriting(int version) =>
        pipelines.TryGetValue(version, out var pipeline)
            ? pipeline
            : throw new BinaryFormatNotSupportedException(
                $"No pipeline registered for format version {version}.");

    /// <summary>
    /// The pipeline for the frame whose first bytes are <paramref name="prefix"/>. Fewer than
    /// <see cref="PrefixLength"/> bytes identify no version.
    /// </summary>
    public IFormatPipeline ForReading(ReadOnlySpan<byte> prefix)
    {
        if (TryReadVersion(prefix, out int version))
            return pipelines.TryGetValue(version, out var pipeline)
                ? pipeline
                : throw new BinaryFormatNotSupportedException(
                    $"No pipeline registered for format version {version}.");

        if (!allowHeaderlessFallback || !pipelines.TryGetValue(V0FormatPipeline.Version, out var headerless))
            throw new BinaryFormatException(
                "Not a recognized BinarySerializer stream (magic number mismatch and the V0 " +
                "fallback is disabled).");

        return headerless;
    }

    /// <summary>
    /// Decodes the version of a versioned frame from its first bytes. Only an exact match of the magic
    /// number identifies one; anything else, including fewer than <see cref="PrefixLength"/> bytes, is
    /// unidentified.
    /// </summary>
    public static bool TryReadVersion(ReadOnlySpan<byte> prefix, out int version)
    {
        if (prefix.Length >= PrefixLength
            && BinaryPrimitives.ReadInt32LittleEndian(prefix) == BinaryFormatConstants.Magic)
        {
            version = BinaryPrimitives.ReadInt32LittleEndian(prefix[sizeof(int)..]);
            return true;
        }

        version = 0;
        return false;
    }
}
