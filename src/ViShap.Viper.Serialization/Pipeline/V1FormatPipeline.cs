using System.Buffers;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// The V1 envelope: header, checksum over the raw payload, compression, then authenticated
/// encryption that binds the header. Every phase size is checked here, before the corresponding
/// buffer exists, and the payload must be consumed exactly on the way back in. Each phase reads one
/// pooled buffer and writes the next; a payload with no phase goes out as the engine wrote it.
/// </summary>
internal sealed class V1FormatPipeline(
    ICompressionAlgorithm compression,
    IChecksumAlgorithm checksum,
    IEncryptionAlgorithm encryption,
    string? keyId,
    AlgorithmCatalog catalog) : IFormatPipeline
{
    /// <summary>The largest associated data image built on the stack rather than rented.</summary>
    private const int StackAssociatedDataBytes = 1024;

    public int Version => BinaryFormatHeaderV1.Version;

    private bool HasPhases =>
        compression.Kind != CompressionAlgorithm.None
        || checksum.Kind != ChecksumAlgorithm.None
        || encryption.Kind != EncryptionAlgorithm.None;

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

            var header = Header(ref state, length, length, onDiskLength: length, checksumBytes: []);
            var headerBytes = WriteHeader(header, ref state);
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
    /// run: its exact length sizes the frame and its key is resolved here, and the ciphertext is
    /// written straight into the destination.
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

            var header = Header(ref state, rawLength, body.Length, onDiskLength: 0, checksumBytes);

            if (encryption.Kind != EncryptionAlgorithm.None)
            {
                var sealedBody = Seal(header, body, ref state);
                body = default;
                return Frame(header with { OnDiskLength = sealedBody.Length }, sealedBody, ref state);
            }

            state.Phases.CheckEncrypted(body.Length, "On-disk payload length");

            var headerBytes = WriteHeader(header with { OnDiskLength = body.Length }, ref state);
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
    /// Prepares the encryption of <paramref name="plaintext"/>: its exact ciphertext length, checked
    /// against the phase limit, the associated data of <paramref name="header"/>, and the key. The
    /// returned body owns <paramref name="plaintext"/>; when this throws, it still belongs to the caller.
    /// </summary>
    private SealedBody Seal(in BinaryFormatHeaderV1 header, RentedBytes plaintext, ref OperationState state)
    {
        var associatedData = default(RentedBytes);
        try
        {
            var service = new EncryptionService(encryption, keyId);

            int ciphertextLength = service.CiphertextLength(plaintext.Length);
            state.Phases.CheckEncrypted(ciphertextLength, "On-disk payload length");

            int length = header.AssociatedDataLength;
            byte[] rented = RentedBytes.RentArray(length);
            associatedData = RentedBytes.Adopt(rented, length);
            header.WriteAssociatedData(rented.AsSpan(0, length));

            var key = service.ResolveKey(state.Keys);
            return new SealedBody(encryption, plaintext, associatedData, key, ciphertextLength);
        }
        catch
        {
            associatedData.Dispose();
            throw;
        }
    }

    /// <summary>A frame of <paramref name="header"/> and an encrypted body, which it owns once returned.</summary>
    private static EncodedFrame Frame(in BinaryFormatHeaderV1 header, SealedBody body, ref OperationState state)
    {
        try
        {
            var headerBytes = WriteHeader(header, ref state);
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

    /// <summary>Releases a phase's input once its output exists.</summary>
    private static RentedBytes Replace(RentedBytes input, RentedBytes output)
    {
        input.Dispose();
        return output;
    }

    private BinaryFormatHeaderV1 Header(
        ref OperationState state,
        int uncompressedLength,
        int compressedLength,
        int onDiskLength,
        byte[] checksumBytes) =>
        new(compression.Kind, compression.CustomName,
            checksum.Kind, checksum.CustomName,
            encryption.Kind, encryption.CustomName,
            keyId,
            state.PreserveReferences,
            uncompressedLength, compressedLength, onDiskLength,
            checksumBytes);

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

    public T? Read<T>(
        ReadOnlySpan<byte> source,
        T? target,
        ref OperationState state,
        out long consumed)
    {
        var budget = WireBudget(ref state);
        var header = ReadHeader(
            HeaderPrefix(source, budget), source.Length, budget, ref state,
            out int headerLength, out var payloadEncryption);

        consumed = headerLength + (long)header.OnDiskLength;

        return Decode(
            header,
            payloadEncryption,
            source.Slice(headerLength, header.OnDiskLength),
            target,
            ref state);
    }

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

        var header = ReadHeader(
            prefix, source.Length, budget, ref state, out int headerLength, out var payloadEncryption);

        consumed = headerLength + (long)header.OnDiskLength;
        var onDisk = source.Slice(headerLength, header.OnDiskLength);

        if (onDisk.IsSingleSegment)
            return Decode(header, payloadEncryption, onDisk.FirstSpan, target, ref state);

        if (header.Encryption == EncryptionAlgorithm.None
            && header.Compression == CompressionAlgorithm.None
            && header.ChecksumAlgorithm == ChecksumAlgorithm.None)
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
            return Decode(header, payloadEncryption, body, target, ref state);
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
    /// policy, and checks the declared on-disk length against the budget and against the bytes that
    /// can still arrive, before anything is allocated for it.
    /// </summary>
    private BinaryFormatHeaderV1 ReadHeader(
        ReadOnlySpan<byte> prefix,
        long available,
        WireBudget budget,
        ref OperationState state,
        out int headerLength,
        out IEncryptionAlgorithm payloadEncryption)
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

        payloadEncryption = catalog.ResolveEncryption(header.Encryption, header.CustomEncryptionName);

        if (state.RequireEncryption && !payloadEncryption.AuthenticatesAssociatedData)
            throw new BinaryIntegrityException(
                "The payload is encrypted with " +
                $"'{header.CustomEncryptionName ?? header.Encryption.ToString()}', which does not " +
                "authenticate format metadata, but this serializer requires encrypted input.");

        CheckOnDiskLength(header, headerLength, available, budget);
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
    /// exactly.
    /// </summary>
    private T? Decode<T>(
        in BinaryFormatHeaderV1 header,
        IEncryptionAlgorithm payloadEncryption,
        ReadOnlySpan<byte> onDisk,
        T? target,
        ref OperationState state)
    {
        var decrypted = default(RentedBytes);
        var decompressed = default(RentedBytes);
        try
        {
            ReadOnlySpan<byte> compressed = onDisk;
            if (payloadEncryption.Kind == EncryptionAlgorithm.None)
            {
                EncryptionService.RequireStored(onDisk, header.CompressedLength);
            }
            else
            {
                decrypted = Decrypt(header, payloadEncryption, onDisk, ref state);
                compressed = decrypted.Span;
            }

            var payloadCompression = catalog.ResolveCompression(header.Compression, header.CustomCompressionName);

            ReadOnlySpan<byte> rawPayload = compressed;
            if (payloadCompression.Kind == CompressionAlgorithm.None)
            {
                CompressionService.RequireStored(compressed, header.UncompressedLength);
            }
            else
            {
                decompressed = CompressionService.Decompress(
                    payloadCompression, compressed, header.UncompressedLength);
                rawPayload = decompressed.Span;
            }

            ChecksumService.Verify(
                catalog.ResolveChecksum(header.ChecksumAlgorithm, header.CustomChecksumName),
                rawPayload,
                header.Checksum);

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
    private static T? DecodePayload<T>(ref WireReader reader, T? target, bool preserveReferences)
    {
        var result = Graph.ReadRoot(ref reader, target, preserveReferences);

        if (reader.Remaining != 0)
            throw new BinaryFormatException(
                $"Payload contains {reader.Remaining} trailing byte(s) after the root value.");

        return result;
    }

    private static RentedBytes Decrypt(
        in BinaryFormatHeaderV1 header,
        IEncryptionAlgorithm payloadEncryption,
        ReadOnlySpan<byte> ciphertext,
        ref OperationState state)
    {
        int length = header.AssociatedDataLength;
        byte[]? rented = null;
        Span<byte> associatedData = length <= StackAssociatedDataBytes
            ? stackalloc byte[StackAssociatedDataBytes]
            : rented = RentedBytes.RentArray(length);

        try
        {
            associatedData = associatedData[..length];
            header.WriteAssociatedData(associatedData);

            return EncryptionService.Decrypt(
                payloadEncryption,
                ciphertext,
                associatedData,
                state.Keys,
                header.KeyId,
                header.CompressedLength);
        }
        finally
        {
            associatedData.Clear();
            if (rented is not null)
                RentedBytes.ReturnArray(rented);
        }
    }
}
