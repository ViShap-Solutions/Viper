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

    public EncodedFrame Write<T>(T data, SerializationOperation operation)
    {
        var payload = new PayloadBuffer(operation.Limits.MaxPayloadBytes, "payload");
        try
        {
            var writer = new WireWriter(payload, operation);
            using (var engine = new GraphWriter(operation))
                engine.WriteRoot(ref writer, data);

            writer.Flush();
            operation.Phases.CheckPayload(payload.Length, "Payload length");

            if (HasPhases)
                return WritePhases(payload, operation);

            int length = (int)payload.Length;
            operation.Phases.CheckCompressed(length, "Compressed payload length");
            operation.Phases.CheckEncrypted(length, "On-disk payload length");

            var header = Header(operation, length, length, onDiskLength: length, checksumBytes: []);
            var headerBytes = WriteHeader(header, operation);
            try
            {
                return EncodedFrame.Of(headerBytes, payload, operation.Limits.MaxWireBytes);
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
    /// output exists; the last output is the body of the frame.
    /// </summary>
    private EncodedFrame WritePhases(PayloadBuffer payload, SerializationOperation operation)
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
                    .Compress(body.Span, operation.Limits.MaxCompressedBytes));

            operation.Phases.CheckCompressed(body.Length, "Compressed payload length");

            var header = Header(operation, rawLength, body.Length, onDiskLength: 0, checksumBytes);

            if (encryption.Kind != EncryptionAlgorithm.None)
                body = Replace(body, Encrypt(header, body.Span, operation));

            operation.Phases.CheckEncrypted(body.Length, "On-disk payload length");

            var headerBytes = WriteHeader(header with { OnDiskLength = body.Length }, operation);
            try
            {
                return EncodedFrame.Of(headerBytes, body, operation.Limits.MaxWireBytes);
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

    private RentedBytes Encrypt(
        in BinaryFormatHeaderV1 header,
        ReadOnlySpan<byte> plaintext,
        SerializationOperation operation)
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

            return new EncryptionService(encryption, keyId).Encrypt(
                plaintext, associatedData, operation.Keys, operation.Limits.MaxEncryptedBytes);
        }
        finally
        {
            associatedData.Clear();
            if (rented is not null)
                RentedBytes.ReturnArray(rented);
        }
    }

    /// <summary>Releases a phase's input once its output exists.</summary>
    private static RentedBytes Replace(RentedBytes input, RentedBytes output)
    {
        input.Dispose();
        return output;
    }

    private BinaryFormatHeaderV1 Header(
        SerializationOperation operation,
        int uncompressedLength,
        int compressedLength,
        int onDiskLength,
        byte[] checksumBytes) =>
        new(compression.Kind, compression.CustomName,
            checksum.Kind, checksum.CustomName,
            encryption.Kind, encryption.CustomName,
            keyId,
            operation.PreserveReferences,
            uncompressedLength, compressedLength, onDiskLength,
            checksumBytes);

    private static PayloadBuffer WriteHeader(in BinaryFormatHeaderV1 header, SerializationOperation operation)
    {
        var headerBytes = new PayloadBuffer(operation.Limits.MaxWireBytes, "wire");
        try
        {
            var writer = new WireWriter(headerBytes, operation);
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

    public object? Read(
        Stream source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(source);

        var budget = new WireBudget("wire", operation.Limits.MaxWireBytes);
        long start = StreamSource.Position(source);
        long available = StreamSource.Remaining(source);

        Span<byte> prefix = stackalloc byte[BinaryFormatHeaderV1.MaxLength];
        prefix = prefix[..(int)Math.Min(prefix.Length, Math.Min(available, budget.Maximum))];
        prefix = prefix[..StreamSource.Read(source, prefix, "the format header")];

        var header = ReadHeader(prefix, available, budget, operation, out int headerLength, out var payloadEncryption);

        StreamSource.Seek(source, start + headerLength);
        byte[] onDisk = RentedBytes.RentArray(header.OnDiskLength);
        try
        {
            var body = onDisk.AsSpan(0, header.OnDiskLength);
            int read = StreamSource.Read(source, body, "the on-disk payload");
            if (read != body.Length)
                throw new BinaryFormatException(
                    $"On-disk payload ended early. Expected {body.Length} bytes, got {read}.");

            return Decode(header, payloadEncryption, body, declaredType, existingInstance, operation);
        }
        finally
        {
            RentedBytes.ReturnArray(onDisk);
        }
    }

    public object? Read(
        ReadOnlySpan<byte> source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation)
    {
        var budget = new WireBudget("wire", operation.Limits.MaxWireBytes);
        var prefix = source[..(int)Math.Min(BinaryFormatHeaderV1.MaxLength, Math.Min(source.Length, budget.Maximum))];

        var header = ReadHeader(prefix, source.Length, budget, operation, out int headerLength, out var payloadEncryption);

        return Decode(
            header,
            payloadEncryption,
            source.Slice(headerLength, header.OnDiskLength),
            declaredType,
            existingInstance,
            operation);
    }

    /// <summary>
    /// Decodes and checks the header from the first bytes of the frame, applies the protection
    /// policy, and checks the declared on-disk length against the budget and against the bytes that
    /// can still arrive, before anything is allocated for it.
    /// </summary>
    private BinaryFormatHeaderV1 ReadHeader(
        ReadOnlySpan<byte> prefix,
        long available,
        WireBudget budget,
        SerializationOperation operation,
        out int headerLength,
        out IEncryptionAlgorithm payloadEncryption)
    {
        var headerReader = new WireReader(prefix, operation, budget);
        var header = BinaryFormatHeaderV1.ReadFrom(ref headerReader);
        headerLength = (int)headerReader.Consumed;

        if (operation.RequireEncryption && header.Encryption == EncryptionAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload is not encrypted, but this serializer requires encrypted input.");

        if (operation.RequireChecksum && header.ChecksumAlgorithm == ChecksumAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload carries no checksum, but this serializer requires one.");

        payloadEncryption = catalog.ResolveEncryption(header.Encryption, header.CustomEncryptionName);

        if (operation.RequireEncryption && !payloadEncryption.AuthenticatesAssociatedData)
            throw new BinaryIntegrityException(
                "The payload is encrypted with " +
                $"'{header.CustomEncryptionName ?? header.Encryption.ToString()}', which does not " +
                "authenticate format metadata, but this serializer requires encrypted input.");

        // Two-phase framing: the declared size is compared with the configured maximum and with the
        // bytes that can still arrive before the buffer for it is allocated.
        if (header.OnDiskLength > Math.Min(budget.Maximum, available) - headerLength)
            throw budget.Exceeded(
                header.OnDiskLength, headerLength, available - headerLength, "On-disk payload");

        return header;
    }

    /// <summary>
    /// Undoes the phases — decryption, decompression, checksum — each into a pooled buffer that is
    /// cleared when the value has been read, then decodes the payload, which must be consumed
    /// exactly.
    /// </summary>
    private object? Decode(
        in BinaryFormatHeaderV1 header,
        IEncryptionAlgorithm payloadEncryption,
        ReadOnlySpan<byte> onDisk,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation)
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
                decrypted = Decrypt(header, payloadEncryption, onDisk, operation);
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

            // The header decides whether the payload uses reference framing, so the engine follows
            // the payload rather than the local configuration.
            var payloadOperation = operation.WithPreserveReferences(header.PreserveReferences);

            var reader = new WireReader(rawPayload, payloadOperation);
            using var engine = new GraphReader(payloadOperation);

            object? result = existingInstance is null
                ? engine.ReadValue(ref reader, declaredType)
                : engine.ReadInto(ref reader, existingInstance, declaredType);

            if (reader.Remaining != 0)
                throw new BinaryFormatException(
                    $"Payload contains {reader.Remaining} trailing byte(s) after the root value.");

            return result;
        }
        finally
        {
            decompressed.Dispose();
            decrypted.Dispose();
        }
    }

    private static RentedBytes Decrypt(
        in BinaryFormatHeaderV1 header,
        IEncryptionAlgorithm payloadEncryption,
        ReadOnlySpan<byte> ciphertext,
        SerializationOperation operation)
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
                operation.Keys,
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
