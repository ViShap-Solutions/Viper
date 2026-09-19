namespace ViShap.Viper.Metadata;

/// <summary>
/// Reads the format metadata of a payload without consuming it.
/// </summary>
/// <remarks>
/// <para>
/// Use it to decide how to read data before committing to it: which key it needs, whether it is
/// compressed, which format version wrote it. The stream position is restored even when the header
/// turns out to be malformed.
/// </para>
/// <para>
/// <see cref="BinarySerializerOptions.FromStream(Stream, byte[], SerializationLimits)"/> builds on
/// this and is usually the more convenient entry point.
/// </para>
/// </remarks>
public static class BinaryFormatInspector
{
    /// <summary>Reads the header metadata of a payload, using the default limits.</summary>
    /// <param name="source">
    /// A seekable stream positioned at the start of a payload. Its position is restored before
    /// returning.
    /// </param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the stream does not start with a recognized
    /// header — for example a version 0 payload, which carries none.
    /// </returns>
    /// <exception cref="NotSupportedException"><paramref name="source"/> cannot seek.</exception>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryStreamException">The stream failed.</exception>
    public static BinaryHeaderInfo? Peek(Stream source) => Peek(source, SerializationLimits.Default);

    /// <summary>Reads the header metadata of a payload.</summary>
    /// <param name="source">
    /// A seekable stream positioned at the start of a payload. Its position is restored before
    /// returning.
    /// </param>
    /// <param name="limits">
    /// The resource policy applied while parsing the header, which bounds the strings it may contain.
    /// </param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the stream does not start with a recognized header.
    /// </returns>
    public static BinaryHeaderInfo? Peek(Stream source, SerializationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        if (!source.CanSeek)
            throw new NotSupportedException($"{nameof(Peek)} needs a seekable stream.");

        long start = source.Position;
        try
        {
            if (!BinaryHeaderPeek.TryPeekMagicAndVersion(source, out int version))
                return null;

            if (version != BinaryFormatHeaderV1.Version)
                throw new BinaryFormatNotSupportedException(
                    $"Format version {version} cannot be inspected.");

            var operation = new SerializationOperation(
                limits,
                keys: null,
                preserveReferences: false,
                requireEncryption: false,
                requireChecksum: false);

            var metered = new MeteredReadStream(source, limits.MaxWireBytes, "format inspection");
            return BinaryFormatHeaderV1.ReadFrom(new ValueReader(metered, operation)).ToInfo();
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to inspect the binary format from the underlying stream.", ex);
        }
        finally
        {
            try
            {
                source.Position = start;
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    "Failed to restore the source stream position after format inspection.", ex);
            }
        }
    }
}