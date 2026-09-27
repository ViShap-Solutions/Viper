namespace ViShap.Viper.Pipeline;

/// <summary>
/// The V1 envelope: header, checksum over the raw payload, compression, then authenticated
/// encryption that binds the header. Every phase size is checked here, before the corresponding
/// buffer exists, and the payload must be consumed exactly on the way back in.
/// </summary>
internal sealed class V1FormatPipeline(
    ICompressionAlgorithm compression,
    IChecksumAlgorithm checksum,
    IEncryptionAlgorithm encryption,
    string? keyId,
    AlgorithmCatalog catalog) : IFormatPipeline
{
    public int Version => BinaryFormatHeaderV1.Version;

    public void Write<T>(Stream destination, T data, SerializationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(destination);

        byte[] rawPayload = WritePayload(data, operation);
        operation.Phases.CheckPayload(rawPayload.LongLength, "Payload length");

        byte[] checksumBytes = new ChecksumService(checksum).Compute(rawPayload);

        byte[] compressed = new CompressionService(compression)
            .Compress(rawPayload, operation.Limits.MaxCompressedBytes);
        operation.Phases.CheckCompressed(compressed.LongLength, "Compressed payload length");

        var header = new BinaryFormatHeaderV1(
            compression.Kind, compression.CustomName,
            checksum.Kind, checksum.CustomName,
            encryption.Kind, encryption.CustomName,
            keyId,
            operation.PreserveReferences,
            rawPayload.Length, compressed.Length, OnDiskLength: 0,
            checksumBytes);

        byte[] onDisk = new EncryptionService(encryption, keyId)
            .Encrypt(
                compressed,
                header.BuildAssociatedData(),
                operation.Keys,
                operation.Limits.MaxEncryptedBytes);

        operation.Phases.CheckEncrypted(onDisk.LongLength, "On-disk payload length");

        using var headerBytes = new PayloadBuffer(operation.Limits.MaxWireBytes, "wire");
        var writer = new WireWriter(headerBytes, operation);
        (header with { OnDiskLength = onDisk.Length }).WriteTo(ref writer);
        writer.Flush();

        var wire = new MeteredWriteStream(destination, operation.Limits.MaxWireBytes, "wire");
        headerBytes.WriteTo(wire);
        wire.Write(onDisk);
        wire.Flush();
    }

    public T? Read<T>(Stream source, SerializationOperation operation) =>
        (T?)Read(source, operation, typeof(T), existingInstance: null);

    public T Read<T>(Stream source, T existingInstance, SerializationOperation operation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        return (T)Read(source, operation, typeof(T), existingInstance)!;
    }

    private static byte[] WritePayload<T>(T data, SerializationOperation operation)
    {
        using var payload = new PayloadBuffer(operation.Limits.MaxPayloadBytes, "payload");
        var writer = new WireWriter(payload, operation);

        new GraphWriter(operation).WriteRoot(ref writer, data);

        writer.Flush();
        return payload.ToArray();
    }

    private object? Read(
        Stream source,
        SerializationOperation operation,
        Type declaredType,
        object? existingInstance)
    {
        var rawPayload = ReadAndUnwrap(source, operation, out bool preserveReferences);

        // The header decides whether the payload uses reference framing, so the engine follows the
        // payload rather than the local configuration.
        var payloadOperation = operation.WithPreserveReferences(preserveReferences);

        var reader = new WireReader(rawPayload, payloadOperation);
        var engine = new GraphReader(payloadOperation);

        object? result = existingInstance is null
            ? engine.ReadValue(ref reader, declaredType)
            : engine.ReadInto(ref reader, existingInstance, declaredType);

        if (reader.Remaining != 0)
            throw new BinaryFormatException(
                $"Payload contains {reader.Remaining} trailing byte(s) after the root value.");

        return result;
    }

    private byte[] ReadAndUnwrap(
        Stream source,
        SerializationOperation operation,
        out bool preserveReferences)
    {
        ArgumentNullException.ThrowIfNull(source);

        var budget = new WireBudget("wire", operation.Limits.MaxWireBytes);
        long start = StreamSource.Position(source);
        long available = StreamSource.Remaining(source);

        Span<byte> prefix = stackalloc byte[BinaryFormatHeaderV1.MaxLength];
        prefix = prefix[..(int)Math.Min(prefix.Length, Math.Min(available, budget.Maximum))];
        prefix = prefix[..StreamSource.Read(source, prefix, "the format header")];

        var headerReader = new WireReader(prefix, operation, budget);
        var header = BinaryFormatHeaderV1.ReadFrom(ref headerReader);
        long headerLength = headerReader.Consumed;
        preserveReferences = header.PreserveReferences;

        if (operation.RequireEncryption && header.Encryption == EncryptionAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload is not encrypted, but this serializer requires encrypted input.");

        if (operation.RequireChecksum && header.ChecksumAlgorithm == ChecksumAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload carries no checksum, but this serializer requires one.");

        var payloadEncryption = catalog.ResolveEncryption(header.Encryption, header.CustomEncryptionName);

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

        StreamSource.Seek(source, start + headerLength);
        byte[] onDisk = new byte[header.OnDiskLength];
        int read = StreamSource.Read(source, onDisk, "the on-disk payload");
        if (read != onDisk.Length)
            throw new BinaryFormatException(
                $"On-disk payload ended early. Expected {onDisk.Length} bytes, got {read}.");

        byte[] compressed = EncryptionService.Decrypt(
            payloadEncryption,
            onDisk,
            header.BuildAssociatedData(),
            operation.Keys,
            header.KeyId,
            header.CompressedLength);

        byte[] rawPayload = CompressionService.Decompress(
            catalog.ResolveCompression(header.Compression, header.CustomCompressionName),
            compressed,
            header.UncompressedLength);

        ChecksumService.Verify(
            catalog.ResolveChecksum(header.ChecksumAlgorithm, header.CustomChecksumName),
            rawPayload,
            header.Checksum);

        return rawPayload;
    }
}
