using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ViShap.Viper.Diagnostics;

/// <summary>
/// Reads a frame for a person, the way JSON or XML can be read by eye: its header, what each phase
/// did, and — given the type it was written as — the payload as a tree of values with the offset and
/// the length of each, down to the byte and the member where a read fails.
/// </summary>
/// <remarks>
/// This is tooling, not production behaviour, so unlike the serializer it reports a failure instead of
/// throwing it: a malformed or hostile frame yields a dump whose <see cref="BinaryDump.Failure"/> is
/// the exception a read raises. The read is the ordinary one, under the limits of the options given —
/// <see cref="SerializationLimits.Default"/> when none are — so a hostile frame costs a dump what it
/// would cost a read. An encrypted frame is decrypted only with the keys of the options given, and a
/// decrypted dump shows the plaintext. Key material is never part of a dump.
/// </remarks>
public static class BinaryFormatDumper
{
    /// <summary>Renders the header of a frame as text.</summary>
    /// <param name="frame">Bytes that start with a frame.</param>
    /// <returns>
    /// The format version, the payload mode, each service record and the declared lengths, or a line
    /// saying why the header could not be read. This method does not throw for malformed input.
    /// </returns>
    public static string DumpHeader(ReadOnlySpan<byte> frame)
    {
        try
        {
            return DescribeHeader(BinaryFormatInspector.Peek(frame));
        }
        catch (BinarySerializerException ex)
        {
            return Unreadable(ex);
        }
    }

    /// <summary>Renders the header of a frame held in one segment or many as text.</summary>
    /// <param name="frame">Bytes that start with a frame.</param>
    /// <returns>The same report as <see cref="DumpHeader(ReadOnlySpan{byte})"/>. This method does not throw for malformed input.</returns>
    public static string DumpHeader(ReadOnlySequence<byte> frame)
    {
        try
        {
            return DescribeHeader(BinaryFormatInspector.Peek(frame));
        }
        catch (BinarySerializerException ex)
        {
            return Unreadable(ex);
        }
    }

    /// <summary>Renders the header of the frame at the position of <paramref name="source"/> as text.</summary>
    /// <param name="source">A seekable stream positioned at the start of a frame. Its position is restored.</param>
    /// <returns>The same report as <see cref="DumpHeader(ReadOnlySpan{byte})"/>. This method does not throw for malformed input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="source"/> cannot seek. Reading a header without consuming the stream needs
    /// seekability, so this is a caller mistake rather than something to report as text.
    /// </exception>
    public static string DumpHeader(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            return DescribeHeader(BinaryFormatInspector.Peek(source));
        }
        catch (BinarySerializerException ex)
        {
            return Unreadable(ex);
        }
    }

    /// <summary>
    /// Reads a frame without a type: its header, then each phase undone with the keys of
    /// <paramref name="options"/> and verified, then the payload as annotated hexadecimal. Bytes that
    /// do not start with a frame are shown as a version 0 payload.
    /// </summary>
    /// <param name="frame">The bytes of one frame.</param>
    /// <param name="options">The limits, the keys and the custom algorithms to read with; <see langword="null"/> for the defaults and no key.</param>
    /// <returns>The dump. This method does not throw for malformed input.</returns>
    public static BinaryDump Dump(ReadOnlySpan<byte> frame, BinarySerializerOptions? options = null) =>
        Read(frame, options, decoder: null, TypeNames.Of(typeof(object)));

    /// <summary>
    /// Reads a frame as <typeparamref name="T"/>: the header and the phases as
    /// <see cref="Dump(ReadOnlySpan{byte}, BinarySerializerOptions?)"/> reports them, and the payload
    /// as a tree of values.
    /// </summary>
    /// <typeparam name="T">The type the frame was written as.</typeparam>
    /// <param name="frame">The bytes of one frame.</param>
    /// <param name="options">The limits, the keys and the custom algorithms to read with; <see langword="null"/> for the defaults and no key.</param>
    /// <returns>The dump; after a failure, the tree up to it. This method does not throw for malformed input.</returns>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static BinaryDump Dump<T>(ReadOnlySpan<byte> frame, BinarySerializerOptions? options = null) =>
        Read(frame, options, new RootDecoder<T>(), TypeNames.Of(typeof(T)));

    /// <summary>Reads a frame held in one segment or many as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type the frame was written as.</typeparam>
    /// <param name="frame">The bytes of one frame.</param>
    /// <param name="options">The limits, the keys and the custom algorithms to read with; <see langword="null"/> for the defaults and no key.</param>
    /// <returns>The dump; after a failure, the tree up to it. This method does not throw for malformed input.</returns>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static BinaryDump Dump<T>(ReadOnlySequence<byte> frame, BinarySerializerOptions? options = null) =>
        frame.IsSingleSegment
            ? Dump<T>(frame.FirstSpan, options)
            : Dump<T>(frame.ToArray(), options);

    /// <summary>Writes <paramref name="value"/> with <paramref name="options"/> and reads back what was written.</summary>
    /// <typeparam name="T">The type to write the value as.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="options">The options to write and read with; <see langword="null"/> for the defaults.</param>
    /// <returns>The dump of the frame the serializer wrote.</returns>
    /// <exception cref="BinarySerializerException">The value cannot be written; the write raises what it always does.</exception>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static BinaryDump DumpValue<T>(T value, BinarySerializerOptions? options = null)
    {
        byte[] frame = new BinarySerializer(options).Serialize(value);
        return Dump<T>(frame, options);
    }

    /// <summary>
    /// The first value at which two frames of <typeparamref name="T"/> differ, walking both trees in
    /// order and comparing each node's kind, type, value, key, union tag and number of children.
    /// Offsets are not compared, so two encryptions of one value, with their different nonces, are
    /// equal.
    /// </summary>
    /// <typeparam name="T">The type both frames were written as.</typeparam>
    /// <param name="expected">The frame expected.</param>
    /// <param name="actual">The frame to compare with it.</param>
    /// <param name="options">The options to read both with; <see langword="null"/> for the defaults and no key.</param>
    /// <returns>The first difference, or <see langword="null"/> when both frames decode to the same tree.</returns>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static BinaryDumpDifference? Compare<T>(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        BinarySerializerOptions? options = null)
    {
        var left = Dump<T>(expected, options);
        var right = Dump<T>(actual, options);

        if (left.Root is null || right.Root is null)
            return left.Root is null && right.Root is null && left.Failure?.GetType() == right.Failure?.GetType()
                ? null
                : new BinaryDumpDifference(typeof(T).Name, left.Root, right.Root);

        return Difference(left.Root, right.Root, left.Root.Name);
    }

    private static BinaryDumpDifference? Difference(BinaryDumpNode expected, BinaryDumpNode actual, string path)
    {
        if (expected.Kind != actual.Kind
            || expected.TypeName != actual.TypeName
            || expected.Value != actual.Value
            || expected.Key != actual.Key
            || expected.UnionTag != actual.UnionTag
            || expected.ChildList.Count != actual.ChildList.Count)
        {
            return new BinaryDumpDifference(path, expected, actual);
        }

        for (int index = 0; index < expected.ChildList.Count; index++)
        {
            var child = expected.ChildList[index];
            string childPath = child.Name.StartsWith('[') || child.Name.StartsWith('{')
                ? path + child.Name
                : path + "." + child.Name;

            if (Difference(child, actual.ChildList[index], childPath) is { } difference)
                return difference;
        }

        return null;
    }

    /// <summary>
    /// Reads a frame: its header, each phase undone, and the payload — as a tree of values when a
    /// <paramref name="decoder"/> is given, as bytes otherwise.
    /// </summary>
    private static BinaryDump Read(
        ReadOnlySpan<byte> frame,
        BinarySerializerOptions? options,
        RootDecoder? decoder,
        string typeName)
    {
        options ??= BinarySerializerOptions.Default;
        var state = new OperationState(options.Limits, options.Keys, false, false, false, options.ContractSet);

        bool framed;
        try
        {
            framed = FormatRouter.TryReadVersion(frame, out _);
        }
        catch (BinarySerializerException ex)
        {
            return new BinaryDump(1, null, frame.Length, frame.ToArray(), payloadIsStored: true, null, null, null, typeName, null, ex);
        }

        return framed
            ? ReadFrame(frame, options, ref state, decoder, typeName)
            : ReadPayload(frame, null, frame.Length, ref state, decoder, typeName, preserveReferences: false, null, null, null);
    }

    private static BinaryDump ReadFrame(
        ReadOnlySpan<byte> frame,
        BinarySerializerOptions options,
        ref OperationState state,
        RootDecoder? decoder,
        string typeName)
    {
        BinaryFormatHeaderV1 header;
        BinaryHeaderInfo info;
        int headerLength;

        try
        {
            if (!FormatRouter.TryReadVersion(frame, out int version) || version != BinaryFormatHeaderV1.Version)
                throw new BinaryFormatNotSupportedException($"Format version {version} cannot be dumped.");

            var budget = new WireBudget("wire", state.Limits.MaxWireBytes);
            var reader = new WireReader(
                frame[..(int)Math.Min(BinaryFormatHeaderV1.MaxLength, Math.Min(frame.Length, budget.Maximum))],
                ref state,
                budget);

            header = BinaryFormatHeaderV1.ReadFrom(ref reader);
            headerLength = (int)reader.Consumed;
            info = header.ToInfo(headerLength);
        }
        catch (BinarySerializerException ex)
        {
            return new BinaryDump(1, null, frame.Length, frame.ToArray(), payloadIsStored: true, null, null, null, typeName, null, ex);
        }

        ReadOnlySpan<byte> headerBytes = frame[..headerLength];
        byte[] stored = frame[headerLength..].ToArray();

        var decrypted = default(RentedBytes);
        var decompressed = default(RentedBytes);
        bool? isDecrypted = header.Encryption == EncryptionAlgorithm.None ? null : false;
        bool? checksumVerified = null;
        int? compressedLength = null;

        try
        {
            if (header.OnDiskLength > stored.Length)
                throw new BinaryFormatException(
                    $"On-disk payload declares {header.OnDiskLength} byte(s) but only {stored.Length} follow the header.");

            if (header.OnDiskLength < stored.Length)
                throw new BinaryFormatException(
                    $"The frame holds {stored.Length - header.OnDiskLength} byte(s) after the end of the payload.");

            var catalog = options.Catalog;
            var encryption = catalog.ResolveEncryption(header.Encryption, header.CustomEncryptionName);
            var checksum = catalog.ResolveChecksum(header.ChecksumAlgorithm, header.CustomChecksumName);
            var compression = catalog.ResolveCompression(header.Compression, header.CustomCompressionName);
            ChecksumService.RequireLength(checksum, header.Checksum);

            ReadOnlySpan<byte> compressed = stored;
            if (encryption.Kind != EncryptionAlgorithm.None)
            {
                decrypted = EncryptionService.Decrypt(encryption, stored, headerBytes, state.Keys, header.KeyId);
                isDecrypted = true;
                header.CheckPlaintext(decrypted.Length, state.Limits);
                compressed = decrypted.Span;
            }

            ReadOnlySpan<byte> raw = compressed;
            if (compression.Kind != CompressionAlgorithm.None)
            {
                compressedLength = compressed.Length;
                decompressed = CompressionService.Decompress(compression, compressed, header.UncompressedLength);
                raw = decompressed.Span;
            }

            if (checksum.Kind != ChecksumAlgorithm.None)
            {
                checksumVerified = false;
                ChecksumService.Verify(checksum, raw, header.Checksum);
                checksumVerified = true;
            }

            return ReadPayload(
                raw, info, frame.Length, ref state, decoder, typeName,
                header.PreserveReferences, isDecrypted, checksumVerified, compressedLength);
        }
        catch (BinarySerializerException ex)
        {
            // The phases stopped before the payload: what is shown is what they had reached.
            byte[] shown = decrypted.Length > 0 ? decrypted.Span.ToArray() : stored;
            return new BinaryDump(
                1, info, frame.Length, shown, payloadIsStored: true,
                compressedLength, checksumVerified, isDecrypted, typeName, null, ex);
        }
        finally
        {
            decompressed.Dispose();
            decrypted.Dispose();
        }
    }

    private static BinaryDump ReadPayload(
        ReadOnlySpan<byte> payload,
        BinaryHeaderInfo? info,
        int frameLength,
        ref OperationState state,
        RootDecoder? decoder,
        string typeName,
        bool preserveReferences,
        bool? decrypted,
        bool? checksumVerified,
        int? compressedLength)
    {
        int version = info is null ? 0 : 1;
        BinarySerializerException? failure = null;
        DumpTrace? trace = null;

        if (decoder is not null)
        {
            trace = new DumpTrace();
            state.Trace = trace;
            try
            {
                var reader = new WireReader(payload, ref state);
                decoder.Decode(ref reader, preserveReferences);

                if (reader.Remaining != 0)
                    throw new BinaryFormatException(
                        $"Payload contains {reader.Remaining} trailing byte(s) after the root value.");
            }
            catch (BinarySerializerException ex)
            {
                failure = ex;
            }
            finally
            {
                state.Trace = null;
            }
        }

        return new BinaryDump(
            version, info, frameLength, payload.ToArray(), payloadIsStored: false,
            compressedLength, checksumVerified, decrypted, typeName, trace, failure);
    }

    private static string DescribeHeader(BinaryHeaderInfo? info)
    {
        var report = new StringBuilder();

        if (info is null)
        {
            report.AppendLine("No recognized Viper header (V0 payload or unrelated data).");
            return report.ToString();
        }

        var header = info.Value;
        report.AppendLine(CultureInfo.InvariantCulture, $"Format version : {header.FormatVersion}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Header length  : {header.HeaderLength}");
        report.AppendLine(CultureInfo.InvariantCulture, $"References     : {(header.PreserveReferences ? "preserved" : "none")}");
        string uncompressed = header.UncompressedLength is { } length
            ? ", " + length.ToString(CultureInfo.InvariantCulture) + " bytes uncompressed"
            : string.Empty;
        string checksum = header.Checksum.IsEmpty ? string.Empty : ", " + Convert.ToHexString(header.Checksum.Span);

        report.AppendLine(CultureInfo.InvariantCulture, $"Compression    : {Describe(header.Compression, header.CustomCompressionName)}{uncompressed}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Checksum       : {Describe(header.ChecksumAlgorithm, header.CustomChecksumName)}{checksum}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Encryption     : {Describe(header.Encryption, header.CustomEncryptionName)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Key id         : {header.KeyId ?? "(none)"}");
        report.AppendLine(CultureInfo.InvariantCulture, $"On-disk length : {header.OnDiskLength}");
        return report.ToString();
    }

    private static string Unreadable(BinarySerializerException ex) =>
        $"Header could not be read: {ex.GetType().Name}: {ex.Message}\n";

    private static string Describe<TKind>(TKind kind, string? customName) where TKind : Enum =>
        customName is null ? kind.ToString() : $"{kind} ('{customName}')";

    /// <summary>Reads the root value of a payload as the type a typed dump names.</summary>
    private abstract class RootDecoder
    {
        public abstract void Decode(ref WireReader reader, bool preserveReferences);
    }

    /// <summary>
    /// Reads the root as <typeparamref name="T"/>, through the engine. Only a typed dump creates one,
    /// so only a typed dump carries the requirements of the reflection path.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private sealed class RootDecoder<T> : RootDecoder
    {
        public override void Decode(ref WireReader reader, bool preserveReferences) =>
            Graph.ReadRoot<T>(ref reader, default, preserveReferences);
    }
}
