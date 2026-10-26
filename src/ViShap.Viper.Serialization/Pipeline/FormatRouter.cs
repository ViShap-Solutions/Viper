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
    /// <summary>The fewest bytes that can identify a versioned frame: the magic number and one byte of the version.</summary>
    public const int PrefixLength = sizeof(int) + 1;

    /// <summary>The most bytes identifying a frame takes: the magic number and the longest 7-bit encoded version.</summary>
    public const int LongestPrefix = sizeof(int) + 5;

    public IFormatPipeline ForWriting(int version) =>
        pipelines.TryGetValue(version, out var pipeline)
            ? pipeline
            : throw new BinaryFormatNotSupportedException(
                $"No pipeline registered for format version {version}.");

    /// <summary>
    /// The pipeline for the frame whose first bytes are <paramref name="prefix"/>, which holds every
    /// byte of the frame's identification that the source has.
    /// </summary>
    /// <exception cref="BinaryFormatException">
    /// The magic number is followed by a version that is not minimally encoded, or the bytes are not
    /// identified and the V0 fallback is off.
    /// </exception>
    /// <exception cref="BinaryFormatNotSupportedException">The version is not one this build reads.</exception>
    public IFormatPipeline ForReading(ReadOnlySpan<byte> prefix)
    {
        if (TryReadVersion(prefix, out int version))
        {
            return version != V0FormatPipeline.Version && pipelines.TryGetValue(version, out var pipeline)
                ? pipeline
                : throw new BinaryFormatNotSupportedException(
                    $"No pipeline registered for format version {version}.");
        }

        if (!allowHeaderlessFallback || !pipelines.TryGetValue(V0FormatPipeline.Version, out var headerless))
            throw new BinaryFormatException(
                "Not a recognized BinarySerializer stream (magic number mismatch and the V0 " +
                "fallback is disabled).");

        return headerless;
    }

    /// <summary>
    /// How many bytes, counted from the start, identifying the frame needs when
    /// <paramref name="buffered"/> are the bytes that have arrived so far. Never more than the frame
    /// the bytes begin holds, so asking a source for them never reaches past it.
    /// </summary>
    public static int Needs(ReadOnlySpan<byte> buffered)
    {
        if (buffered.Length < PrefixLength)
            return PrefixLength;

        if (!HasMagic(buffered))
            return 0;

        return WireReader.TryDecode7BitEncodedInt(buffered[sizeof(int)..], out _, out int used) == SevenBitStatus.Incomplete
            ? sizeof(int) + used + 1
            : 0;
    }

    /// <summary>
    /// Decodes the version of a versioned frame from its first bytes. Only the magic number followed
    /// by a whole version identifies one; bytes that end before the version is complete — too few to
    /// be any frame — are unidentified, as is anything without the magic.
    /// </summary>
    /// <exception cref="BinaryFormatException">The magic number is followed by a version that is not minimally encoded.</exception>
    public static bool TryReadVersion(ReadOnlySpan<byte> prefix, out int version)
    {
        version = 0;
        if (!HasMagic(prefix))
            return false;

        return WireReader.TryDecode7BitEncodedInt(prefix[sizeof(int)..], out version, out _) switch
        {
            SevenBitStatus.Complete => true,
            SevenBitStatus.Incomplete => false,
            _ => throw new BinaryFormatException("The format version is not a minimally encoded 7-bit integer.")
        };
    }

    private static bool HasMagic(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= sizeof(int)
        && BinaryPrimitives.ReadInt32LittleEndian(prefix) == BinaryFormatConstants.Magic;
}
