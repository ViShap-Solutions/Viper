namespace ViShap.Viper.Pipeline;

/// <summary>
/// The V0 envelope: a headerless payload and nothing else, for callers who want the smallest
/// encoding the engine can produce and already know out of band what the bytes are — a private or
/// tightly coordinated transport, IPC, or a protocol that supplies its own framing.
/// </summary>
/// <remarks>
/// The payload is written as-is: no magic number, no version, no algorithm or length metadata. That
/// is what makes it compact and what makes it not self-describing, so reading one is an explicit
/// decision rather than an inference from bytes. Without a header there is nowhere to record which
/// reference framing or which compression, checksum or encryption phase produced the bytes, so V0
/// has none of them; everything the payload itself can express — the full type set, unions, keyed
/// contracts, depth, budgets and metering — applies unchanged. Keyed contracts patch each field's
/// length after writing it, and V0 writes straight to the destination rather than buffering, so a
/// keyed write needs the destination stream to be seekable. Unlike V1 it may be embedded in a larger
/// stream, which is why it does not require the source to end with the payload.
/// </remarks>
internal sealed class V0FormatPipeline : IFormatPipeline
{
    /// <summary>The wire format version this pipeline reads and writes.</summary>
    public const int Version = 0;

    int IFormatPipeline.Version => Version;

    public void Write<T>(Stream destination, T data, SerializationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var wire = new MeteredWriteStream(destination, operation.Limits.MaxWireBytes, "wire");
        var payload = new MeteredWriteStream(wire, operation.Limits.MaxPayloadBytes, "payload");
        var writer = new ValueWriter(payload, operation);

        new GraphWriter(writer, WithoutReferences(operation)).WriteRoot(data);

        writer.Flush();
    }

    public T? Read<T>(Stream source, SerializationOperation operation)
    {
        var reader = OpenReader(source, operation, out var payloadOperation);
        return new GraphReader(reader, payloadOperation).ReadRoot<T>();
    }

    public T Read<T>(Stream source, T existingInstance, SerializationOperation operation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        var reader = OpenReader(source, operation, out var payloadOperation);
        return (T)new GraphReader(reader, payloadOperation).ReadInto(existingInstance, typeof(T));
    }

    private static ValueReader OpenReader(
        Stream source,
        SerializationOperation operation,
        out SerializationOperation payloadOperation)
    {
        ArgumentNullException.ThrowIfNull(source);

        payloadOperation = WithoutReferences(operation);

        // Both ceilings apply on the way in, exactly as they do on the way out.
        var wire = new MeteredReadStream(source, operation.Limits.MaxWireBytes, "wire");
        var payload = new MeteredReadStream(wire, operation.Limits.MaxPayloadBytes, "payload");
        return new ValueReader(payload, payloadOperation);
    }

    // No header can record that a payload uses reference framing, so V0 never emits or expects it.
    private static SerializationOperation WithoutReferences(SerializationOperation operation) =>
        operation.WithPreserveReferences(false);
}
