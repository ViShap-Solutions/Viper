using System.Buffers;

namespace ViShap.Viper.Metadata;

/// <summary>
/// Reads the format metadata of a payload without reading the payload itself.
/// </summary>
/// <remarks>
/// Use it for diagnostics, or to decide how to read data before committing to it: which key it needs,
/// whether it is compressed, which format version wrote it. Reading a version 1 frame never needs it —
/// the serializer takes the algorithms from the header on its own, and keys for reading are supplied
/// with <see cref="BinarySerializerOptionsBuilder.WithKeys(Func{string, byte[]})"/>.
/// </remarks>
public static class BinaryFormatInspector
{
    /// <summary>Reads the header metadata at the start of <paramref name="source"/>, using the default limits.</summary>
    /// <param name="source">Bytes that start with a payload.</param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the bytes do not start with a recognized header —
    /// for example a version 0 payload, which carries none.
    /// </returns>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryLimitException">The header declares more than the default limits allow.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// The header names a format version or an algorithm this build cannot read.
    /// </exception>
    public static BinaryHeaderInfo? Peek(ReadOnlySpan<byte> source) => Peek(source, SerializationLimits.Default);

    /// <summary>Reads the header metadata at the start of <paramref name="source"/>.</summary>
    /// <param name="source">Bytes that start with a payload.</param>
    /// <param name="limits">
    /// The resource policy applied while parsing the header, which bounds the lengths it declares.
    /// </param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the bytes do not start with a recognized header.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException"><paramref name="limits"/> holds a value that is not positive.</exception>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryLimitException">The header declares more than <paramref name="limits"/> allows.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// The header names a format version or an algorithm this build cannot read.
    /// </exception>
    public static BinaryHeaderInfo? Peek(ReadOnlySpan<byte> source, SerializationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        return Inspect(source, limits);
    }

    /// <summary>Reads the header metadata at the start of <paramref name="source"/>, using the default limits.</summary>
    /// <param name="source">Bytes that start with a payload, in one segment or many.</param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the bytes do not start with a recognized header.
    /// </returns>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryLimitException">The header declares more than the default limits allow.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// The header names a format version or an algorithm this build cannot read.
    /// </exception>
    public static BinaryHeaderInfo? Peek(ReadOnlySequence<byte> source) => Peek(source, SerializationLimits.Default);

    /// <summary>Reads the header metadata at the start of <paramref name="source"/>.</summary>
    /// <param name="source">Bytes that start with a payload, in one segment or many.</param>
    /// <param name="limits">
    /// The resource policy applied while parsing the header, which bounds the lengths it declares.
    /// </param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the bytes do not start with a recognized header.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException"><paramref name="limits"/> holds a value that is not positive.</exception>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryLimitException">The header declares more than <paramref name="limits"/> allows.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// The header names a format version or an algorithm this build cannot read.
    /// </exception>
    public static BinaryHeaderInfo? Peek(ReadOnlySequence<byte> source, SerializationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        Span<byte> prefix = stackalloc byte[BinaryFormatHeaderV1.MaxLength];
        prefix = prefix[..(int)Math.Min(prefix.Length, source.Length)];
        source.Slice(0, prefix.Length).CopyTo(prefix);

        return Inspect(prefix, limits);
    }

    /// <summary>Reads the header metadata of a payload, using the default limits.</summary>
    /// <param name="source">
    /// A seekable stream positioned at the start of a payload. Its position is restored before
    /// returning.
    /// </param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the stream does not start with a recognized
    /// header — for example a version 0 payload, which carries none.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException"><paramref name="source"/> cannot seek.</exception>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryLimitException">The header declares more than the default limits allow.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// The header names a format version or an algorithm this build cannot read.
    /// </exception>
    /// <exception cref="BinaryStreamException">The stream failed.</exception>
    public static BinaryHeaderInfo? Peek(Stream source) => Peek(source, SerializationLimits.Default);

    /// <summary>Reads the header metadata of a payload.</summary>
    /// <param name="source">
    /// A seekable stream positioned at the start of a payload. Its position is restored before
    /// returning, also when the header is malformed.
    /// </param>
    /// <param name="limits">
    /// The resource policy applied while parsing the header, which bounds the lengths it declares.
    /// </param>
    /// <returns>
    /// The metadata, or <see langword="null"/> when the stream does not start with a recognized header.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="limits"/> is null.</exception>
    /// <exception cref="BinaryConfigurationException"><paramref name="limits"/> holds a value that is not positive.</exception>
    /// <exception cref="NotSupportedException"><paramref name="source"/> cannot seek.</exception>
    /// <exception cref="BinaryFormatException">The header is recognized but malformed.</exception>
    /// <exception cref="BinaryLimitException">The header declares more than <paramref name="limits"/> allows.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// The header names a format version or an algorithm this build cannot read.
    /// </exception>
    /// <exception cref="BinaryStreamException">The stream failed.</exception>
    public static BinaryHeaderInfo? Peek(Stream source, SerializationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        if (!source.CanSeek)
            throw new NotSupportedException($"{nameof(Peek)} needs a seekable stream.");

        long start;
        try
        {
            start = source.Position;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to inspect the binary format from the underlying stream.", ex);
        }

        try
        {
            Span<byte> prefix = stackalloc byte[BinaryFormatHeaderV1.MaxLength];
            prefix = prefix[..(int)Math.Min(prefix.Length, Math.Max(0, source.Length - start))];
            prefix = prefix[..ReadPrefix(source, prefix)];

            return Inspect(prefix, limits);
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

    /// <summary>Decodes the header at the start of <paramref name="prefix"/>, which holds at least all of it that is there.</summary>
    private static BinaryHeaderInfo? Inspect(ReadOnlySpan<byte> prefix, SerializationLimits limits)
    {
        if (!FormatRouter.TryReadVersion(prefix, out int version))
            return null;

        if (version != BinaryFormatHeaderV1.Version)
            throw new BinaryFormatNotSupportedException(
                $"Format version {version} cannot be inspected.");

        var state = new OperationState(
            limits,
            keys: null,
            preserveReferences: false,
            requireEncryption: false,
            requireChecksum: false);

        var budget = new WireBudget("format inspection", limits.MaxWireBytes);
        var reader = new WireReader(
            prefix[..(int)Math.Min(prefix.Length, budget.Maximum)], ref state, budget);

        var header = BinaryFormatHeaderV1.ReadFrom(ref reader);
        return header.ToInfo((int)reader.Consumed);
    }

    private static int ReadPrefix(Stream source, Span<byte> prefix)
    {
        int total = 0;
        while (total < prefix.Length)
        {
            int read = source.Read(prefix[total..]);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }
}
