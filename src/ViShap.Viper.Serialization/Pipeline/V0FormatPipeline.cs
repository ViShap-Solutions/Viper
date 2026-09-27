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
/// contracts, depth, budgets and metering — applies unchanged. The payload is built in the
/// serializer's own buffer and copied to the destination once, so a keyed contract, whose field
/// lengths are patched after each field is written, works with any destination, and a failed write
/// leaves nothing in it. Unlike V1 it may be embedded in a larger stream: reading takes bytes ahead
/// from the source and puts its position back where the payload ends, so it does not require the
/// source to end with the payload.
/// </remarks>
internal sealed class V0FormatPipeline : IFormatPipeline
{
    /// <summary>The wire format version this pipeline reads and writes.</summary>
    public const int Version = 0;

    int IFormatPipeline.Version => Version;

    public void Write<T>(Stream destination, T data, SerializationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var budget = Budget(operation);
        using var payload = new PayloadBuffer(budget.Maximum, budget.Resource);

        var writer = new WireWriter(payload, operation);
        new GraphWriter(WithoutReferences(operation)).WriteRoot(ref writer, data);
        writer.Flush();

        var wire = new MeteredWriteStream(destination, operation.Limits.MaxWireBytes, "wire");
        payload.WriteTo(wire);
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

    private static object? Read(
        Stream source,
        SerializationOperation operation,
        Type declaredType,
        object? existingInstance)
    {
        ArgumentNullException.ThrowIfNull(source);

        var payloadOperation = WithoutReferences(operation);
        var budget = Budget(operation);

        long start = StreamSource.Position(source);
        byte[] buffer = StreamSource.ReadAhead(source, budget.Maximum, "payload", out int length);
        try
        {
            var reader = new WireReader(buffer.AsSpan(0, length), payloadOperation, budget);
            var engine = new GraphReader(payloadOperation);

            object? result = existingInstance is null
                ? engine.ReadValue(ref reader, declaredType)
                : engine.ReadInto(ref reader, existingInstance, declaredType);

            StreamSource.Seek(source, start + reader.Consumed);
            return result;
        }
        finally
        {
            StreamSource.Return(buffer);
        }
    }

    /// <summary>
    /// Without a header the payload is everything that reaches the wire, so the payload and wire
    /// budgets bound the same bytes and the tighter of the two applies, in both directions.
    /// </summary>
    private static WireBudget Budget(SerializationOperation operation) =>
        operation.Limits.MaxWireBytes < operation.Limits.MaxPayloadBytes
            ? new WireBudget("wire", operation.Limits.MaxWireBytes)
            : new WireBudget("payload", operation.Limits.MaxPayloadBytes);

    // No header can record that a payload uses reference framing, so V0 never emits or expects it.
    private static SerializationOperation WithoutReferences(SerializationOperation operation) =>
        operation.WithPreserveReferences(false);
}
