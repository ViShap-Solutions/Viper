using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// The V1 envelope: a header of service records, then the payload after its phases — checksum over
/// the raw payload, compression, then authenticated encryption whose associated data is the exact
/// bytes of the header. Every phase size is checked before the corresponding buffer exists, in the
/// order the header allows, and the payload must be consumed exactly on the way back in. Each phase
/// reads one pooled buffer and writes the next; a payload with no phase goes out as the engine wrote
/// it.
/// </summary>
internal sealed class V1FormatPipeline(
    ICompressionAlgorithm compression,
    IChecksumAlgorithm checksum,
    IEncryptionAlgorithm encryption,
    string? keyId,
    AlgorithmCatalog catalog) : IFormatPipeline
{
    public int Version => BinaryFormatHeaderV1.Version;

    private bool HasPhases =>
        compression.Kind != CompressionAlgorithm.None
        || checksum.Kind != ChecksumAlgorithm.None
        || encryption.Kind != EncryptionAlgorithm.None;

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public EncodedFrame Write<T>(T data, ref OperationState state)
    {
        var payload = new PayloadBuffer(state.Limits.MaxPayloadBytes, "payload");
        try
        {
            var writer = new WireWriter(payload, ref state);
            Graph.WriteRoot(ref writer, data, state.PreserveReferences);

            writer.Flush();
            state.Phases.CheckPayload(payload.Length, "Payload length");

            if (HasPhases)
                return WritePhases(payload, ref state);

            int length = (int)payload.Length;
            state.Phases.CheckCompressed(length, "Compressed payload length");
            state.Phases.CheckEncrypted(length, "On-disk payload length");

            var headerBytes = WriteHeader(Header(ref state, [], uncompressedLength: 0, onDiskLength: length), ref state);
            try
            {
                return EncodedFrame.Of(headerBytes, payload, state.Limits.MaxWireBytes);
            }
            catch
            {
                headerBytes.Dispose();
                throw;
            }
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs checksum, compression and encryption over the payload, which is made contiguous once
    /// because each phase works on a single span. Each phase's input is released as soon as its
    /// output exists; the last output is the body of the frame. Encryption is prepared rather than
    /// run: its exact length completes the header, whose bytes become the associated data, its key is
    /// resolved here, and the ciphertext is written straight into the destination.
    /// </summary>
    private EncodedFrame WritePhases(PayloadBuffer payload, ref OperationState state)
    {
        int rawLength = (int)payload.Length;
        byte[] linear = RentedBytes.RentArray(rawLength);
        payload.CopyTo(linear);
        payload.Dispose();

        var body = RentedBytes.Adopt(linear, rawLength);
        try
        {
            byte[] checksumBytes = checksum.Kind == ChecksumAlgorithm.None
                ? []
                : new ChecksumService(checksum).Compute(body.Span);

            if (compression.Kind != CompressionAlgorithm.None)
                body = Replace(body, new CompressionService(compression)
                    .Compress(body.Span, state.Limits.MaxCompressedBytes));

            state.Phases.CheckCompressed(body.Length, "Compressed payload length");

            if (encryption.Kind != EncryptionAlgorithm.None)
            {
                var service = new EncryptionService(encryption, keyId);
                int ciphertextLength = service.CiphertextLength(body.Length);
                state.Phases.CheckEncrypted(ciphertextLength, "On-disk payload length");

                var sealedHeader = WriteHeader(
                    Header(ref state, checksumBytes, rawLength, onDiskLength: ciphertextLength), ref state);

                var sealedBody = Seal(service, sealedHeader, body, ciphertextLength, ref state);
                body = default;
                return Frame(sealedHeader, sealedBody, ref state);
            }

            state.Phases.CheckEncrypted(body.Length, "On-disk payload length");

            var headerBytes = WriteHeader(Header(ref state, checksumBytes, rawLength, onDiskLength: body.Length), ref state);
            try
            {
                return EncodedFrame.Of(headerBytes, body, state.Limits.MaxWireBytes);
            }
            catch
            {
                headerBytes.Dispose();
                throw;
            }
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Prepares the encryption of <paramref name="plaintext"/> under the header already written: its
    /// bytes, copied, are the associated data, and the key is resolved. The returned body owns
    /// <paramref name="plaintext"/>; when this throws, <paramref name="plaintext"/> still belongs to
    /// the caller and <paramref name="header"/> has been disposed.
    /// </summary>
    private static SealedBody Seal(
        EncryptionService service,
        PayloadBuffer header,
        RentedBytes plaintext,
        int ciphertextLength,
        ref OperationState state)
    {
        var associatedData = default(RentedBytes);
        try
        {
            int length = (int)header.Length;
            byte[] rented = RentedBytes.RentArray(length);
            associatedData = RentedBytes.Adopt(rented, length);
            header.CopyTo(rented);

            var key = service.ResolveKey(state.Keys);
            return new SealedBody(service.Algorithm, plaintext, associatedData, key, ciphertextLength);
        }
        catch
        {
            associatedData.Dispose();
            header.Dispose();
            throw;
        }
    }

    /// <summary>A frame of <paramref name="header"/> and an encrypted body, which it owns once returned.</summary>
    private static EncodedFrame Frame(PayloadBuffer header, SealedBody body, ref OperationState state)
    {
        try
        {
            return EncodedFrame.Of(header, body, state.Limits.MaxWireBytes);
        }
        catch
        {
            header.Dispose();
            body.Dispose();
            throw;
        }
    }

    /// <summary>Releases a phase's input once its output exists.</summary>
    private static RentedBytes Replace(RentedBytes input, RentedBytes output)
    {
        input.Dispose();
        return output;
    }

    private BinaryFormatHeaderV1 Header(
        ref OperationState state,
        byte[] checksumBytes,
        int uncompressedLength,
        int onDiskLength) =>
        new(state.PreserveReferences,
            checksum.Kind, checksum.CustomName, checksumBytes,
            compression.Kind, compression.CustomName, uncompressedLength,
            encryption.Kind, encryption.CustomName, encryption.Kind == EncryptionAlgorithm.None ? null : keyId,
            onDiskLength);

    private static PayloadBuffer WriteHeader(in BinaryFormatHeaderV1 header, ref OperationState state)
    {
        var headerBytes = new PayloadBuffer(state.Limits.MaxWireBytes, "wire");
        try
        {
            var writer = new WireWriter(headerBytes, ref state);
            header.WriteTo(ref writer);
            writer.Flush();
            return headerBytes;
        }
        catch
        {
            headerBytes.Dispose();
            throw;
        }
    }

    public FrameExtent Measure(
        ReadOnlySpan<byte> buffered,
        bool sourceEnded,
        long available,
        ref OperationState state)
    {
        var budget = WireBudget(ref state);

        if (!BinaryFormatHeaderV1.TryMeasure(buffered, out int required))
        {
            long target = Math.Min(required, budget.Maximum);
            if (!sourceEnded && buffered.Length < target)
                return FrameExtent.NeedMore(target);
        }

        var headerReader = new WireReader(HeaderPrefix(buffered, budget), ref state, budget);
        var header = BinaryFormatHeaderV1.ReadFrom(ref headerReader);
        int headerLength = (int)headerReader.Consumed;

        CheckOnDiskLength(header, headerLength, available, budget);
        return FrameExtent.Known(headerLength + (long)header.OnDiskLength);
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Read<T>(
        ReadOnlySpan<byte> source,
        T? target,
        ref OperationState state,
        out long consumed)
    {
        var budget = WireBudget(ref state);
        var header = ReadHeader(
            HeaderPrefix(source, budget), source.Length, budget, ref state,
            out int headerLength, out var phases);

        consumed = headerLength + (long)header.OnDiskLength;

        return Decode(
            header,
            phases,
            source[..headerLength],
            source.Slice(headerLength, header.OnDiskLength),
            target,
            ref state);
    }

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public T? Read<T>(
        ReadOnlySequence<byte> source,
        T? target,
        ref OperationState state,
        out long consumed)
    {
        if (source.IsSingleSegment)
            return Read(source.FirstSpan, target, ref state, out consumed);

        var budget = WireBudget(ref state);

        Span<byte> prefix = stackalloc byte[BinaryFormatHeaderV1.MaxLength];
        prefix = prefix[..(int)Math.Min(prefix.Length, Math.Min(source.Length, budget.Maximum))];
        source.Slice(0, prefix.Length).CopyTo(prefix);

        var header = ReadHeader(prefix, source.Length, budget, ref state, out int headerLength, out var phases);
        var headerBytes = prefix[..headerLength];

        consumed = headerLength + (long)header.OnDiskLength;
        var onDisk = source.Slice(headerLength, header.OnDiskLength);

        if (onDisk.IsSingleSegment)
            return Decode(header, phases, headerBytes, onDisk.FirstSpan, target, ref state);

        if (!phases.Any)
        {
            var reader = new WireReader(onDisk, ref state);
            return DecodePayload(ref reader, target, header.PreserveReferences);
        }

        // Every phase works on one contiguous span, so a segmented body is made contiguous once.
        byte[] linear = RentedBytes.RentArray(header.OnDiskLength);
        try
        {
            var body = linear.AsSpan(0, header.OnDiskLength);
            onDisk.CopyTo(body);
            return Decode(header, phases, headerBytes, body, target, ref state);
        }
        finally
        {
            RentedBytes.ReturnArray(linear);
        }
    }

    private static WireBudget WireBudget(ref OperationState state) =>
        new("wire", state.Limits.MaxWireBytes);

    /// <summary>The bytes a header can occupy at the start of <paramref name="source"/>, within the budget.</summary>
    private static ReadOnlySpan<byte> HeaderPrefix(ReadOnlySpan<byte> source, WireBudget budget) =>
        source[..(int)Math.Min(BinaryFormatHeaderV1.MaxLength, Math.Min(source.Length, budget.Maximum))];

    /// <summary>
    /// Decodes and checks the header from the first bytes of the frame, applies the protection
    /// policy, resolves the algorithms the header names, and checks the declared on-disk length
    /// against the budget and against the bytes that can still arrive, before anything is allocated
    /// for it.
    /// </summary>
    private BinaryFormatHeaderV1 ReadHeader(
        ReadOnlySpan<byte> prefix,
        long available,
        WireBudget budget,
        ref OperationState state,
        out int headerLength,
        out PayloadPhases phases)
    {
        var headerReader = new WireReader(prefix, ref state, budget);
        var header = BinaryFormatHeaderV1.ReadFrom(ref headerReader);
        headerLength = (int)headerReader.Consumed;

        if (state.RequireEncryption && header.Encryption == EncryptionAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload is not encrypted, but this serializer requires encrypted input.");

        if (state.RequireChecksum && header.ChecksumAlgorithm == ChecksumAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload carries no checksum, but this serializer requires one.");

        var payloadEncryption = catalog.ResolveEncryption(header.Encryption, header.CustomEncryptionName);

        if (state.RequireEncryption && !payloadEncryption.AuthenticatesAssociatedData)
            throw new BinaryIntegrityException(
                "The payload is encrypted with " +
                $"'{header.CustomEncryptionName ?? header.Encryption.ToString()}', which does not " +
                "authenticate format metadata, but this serializer requires encrypted input.");

        var payloadChecksum = catalog.ResolveChecksum(header.ChecksumAlgorithm, header.CustomChecksumName);
        ChecksumService.RequireLength(payloadChecksum, header.Checksum);

        var payloadCompression = catalog.ResolveCompression(header.Compression, header.CustomCompressionName);

        CheckOnDiskLength(header, headerLength, available, budget);

        phases = new PayloadPhases(payloadCompression, payloadChecksum, payloadEncryption);
        return header;
    }

    /// <summary>
    /// Two-phase framing: the declared size is compared with the configured maximum and with the
    /// bytes that can still arrive before a buffer is allocated for it or its bytes are waited for.
    /// </summary>
    private static void CheckOnDiskLength(
        in BinaryFormatHeaderV1 header,
        int headerLength,
        long available,
        WireBudget budget)
    {
        if (header.OnDiskLength > Math.Min(budget.Maximum, available) - headerLength)
            throw budget.Exceeded(
                header.OnDiskLength, headerLength, available - headerLength, "On-disk payload");
    }

    /// <summary>
    /// Undoes the phases — decryption, decompression, checksum — each into a pooled buffer that is
    /// cleared when the value has been read, then decodes the payload, which must be consumed
    /// exactly. The plaintext of an encrypted frame is checked against its limits before it is
    /// decompressed, which is the one allocation the decompression ratio protects.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private static T? Decode<T>(
        in BinaryFormatHeaderV1 header,
        PayloadPhases phases,
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> onDisk,
        T? target,
        ref OperationState state)
    {
        var decrypted = default(RentedBytes);
        var decompressed = default(RentedBytes);
        try
        {
            ReadOnlySpan<byte> compressed = onDisk;
            if (phases.Encryption.Kind != EncryptionAlgorithm.None)
            {
                decrypted = EncryptionService.Decrypt(
                    phases.Encryption, onDisk, headerBytes, state.Keys, header.KeyId);
                header.CheckPlaintext(decrypted.Length, state.Limits);
                compressed = decrypted.Span;
            }

            ReadOnlySpan<byte> rawPayload = compressed;
            if (phases.Compression.Kind != CompressionAlgorithm.None)
            {
                decompressed = CompressionService.Decompress(
                    phases.Compression, compressed, header.UncompressedLength);
                rawPayload = decompressed.Span;
            }

            ChecksumService.Verify(phases.Checksum, rawPayload, header.Checksum);

            var reader = new WireReader(rawPayload, ref state);
            return DecodePayload(ref reader, target, header.PreserveReferences);
        }
        finally
        {
            decompressed.Dispose();
            decrypted.Dispose();
        }
    }

    /// <summary>
    /// Reads the root value, which must consume the payload exactly. The header decides whether the
    /// payload uses reference framing, so the engine follows the payload rather than the local
    /// configuration.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    private static T? DecodePayload<T>(ref WireReader reader, T? target, bool preserveReferences)
    {
        var result = Graph.ReadRoot(ref reader, target, preserveReferences);

        if (reader.Remaining != 0)
            throw new BinaryFormatException(
                $"Payload contains {reader.Remaining} trailing byte(s) after the root value.");

        return result;
    }

    /// <summary>The algorithms the services of a frame being read name.</summary>
    private readonly struct PayloadPhases(
        ICompressionAlgorithm compression,
        IChecksumAlgorithm checksum,
        IEncryptionAlgorithm encryption)
    {
        public ICompressionAlgorithm Compression { get; } = compression;

        public IChecksumAlgorithm Checksum { get; } = checksum;

        public IEncryptionAlgorithm Encryption { get; } = encryption;

        /// <summary>Whether the frame names any phase at all.</summary>
        public bool Any =>
            Compression.Kind != CompressionAlgorithm.None
            || Checksum.Kind != ChecksumAlgorithm.None
            || Encryption.Kind != EncryptionAlgorithm.None;
    }
}
