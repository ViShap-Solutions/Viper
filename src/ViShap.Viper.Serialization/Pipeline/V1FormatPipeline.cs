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
    private const bool KeyedContractsSupported = true;

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

        var wire = new MeteredWriteStream(destination, operation.Limits.MaxWireBytes, "wire");
        var writer = new ValueWriter(wire, operation);

        (header with { OnDiskLength = onDisk.Length }).WriteTo(writer);
        writer.Write(onDisk);
        writer.Flush();
    }

    public T? Read<T>(Stream source, SerializationOperation operation)
    {
        var rawPayload = ReadAndUnwrap(source, operation, out bool preserveReferences);
        return ReadPayload(rawPayload, preserveReferences, operation,
            engine => engine.ReadRoot<T>());
    }

    public T Read<T>(Stream source, T existingInstance, SerializationOperation operation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        var rawPayload = ReadAndUnwrap(source, operation, out bool preserveReferences);
        return ReadPayload(rawPayload, preserveReferences, operation,
            engine => (T)engine.ReadInto(existingInstance, typeof(T)))!;
    }

    private static byte[] WritePayload<T>(T data, SerializationOperation operation)
    {
        using var buffer = new MemoryStream();
        var payload = new MeteredWriteStream(buffer, operation.Limits.MaxPayloadBytes, "payload");
        var writer = new ValueWriter(payload, operation);

        new GraphWriter(writer, operation, KeyedContractsSupported).WriteRoot(data);

        writer.Flush();
        return buffer.ToArray();
    }

    private static TResult ReadPayload<TResult>(
        byte[] rawPayload,
        bool preserveReferences,
        SerializationOperation operation,
        Func<GraphReader, TResult> read)
    {
        // The header decides whether the payload uses reference framing, so the engine follows the
        // payload rather than the local configuration.
        var payloadOperation = operation.WithPreserveReferences(preserveReferences);

        using var buffer = new MemoryStream(rawPayload, writable: false);
        var reader = new ValueReader(buffer, payloadOperation);
        var engine = new GraphReader(reader, payloadOperation, KeyedContractsSupported);

        var result = read(engine);

        if (buffer.Position != rawPayload.Length)
            throw new BinaryFormatException(
                $"Payload contains {rawPayload.Length - buffer.Position} trailing byte(s) after the " +
                "root value.");

        return result;
    }

    private byte[] ReadAndUnwrap(
        Stream source,
        SerializationOperation operation,
        out bool preserveReferences)
    {
        ArgumentNullException.ThrowIfNull(source);

        var wire = new MeteredReadStream(source, operation.Limits.MaxWireBytes, "wire");
        var reader = new ValueReader(wire, operation);

        var header = BinaryFormatHeaderV1.ReadFrom(reader);
        preserveReferences = header.PreserveReferences;

        if (operation.RequireEncryption && header.Encryption == EncryptionAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload is not encrypted, but this serializer requires encrypted input.");

        if (operation.RequireChecksum && header.ChecksumAlgorithm == ChecksumAlgorithm.None)
            throw new BinaryIntegrityException(
                "The payload carries no checksum, but this serializer requires one.");

        var payloadEncryption = catalog.ResolveEncryption(header.Encryption, header.CustomEncryptionName);
        if (operation.RequireEncryption && !payloadEncryption.AuthenticatesAssociatedData)
            throw new BinaryConfigurationException(
                $"Encryption algorithm '{header.Encryption}' does not authenticate format metadata, " +
                "so it cannot satisfy RequireEncryption.");

        // Two-phase framing: the declared size is compared with the configured maximum and with the
        // bytes that can still arrive, before the buffer for it is allocated.
        reader.RequireAvailable(header.OnDiskLength, "On-disk payload");
        byte[] onDisk = reader.ReadBytes(header.OnDiskLength, "On-disk payload");

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
