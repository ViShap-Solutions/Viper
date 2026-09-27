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

    public EncodedFrame Write<T>(T data, SerializationOperation operation)
    {
        var budget = Budget(operation);
        var payload = new PayloadBuffer(budget.Maximum, budget.Resource);
        try
        {
            var writer = new WireWriter(payload, operation);
            using (var engine = new GraphWriter(WithoutReferences(operation)))
                engine.WriteRoot(ref writer, data);

            writer.Flush();
            return EncodedFrame.Of(header: null, payload, operation.Limits.MaxWireBytes);
        }
        catch
        {
            payload.Dispose();
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

        var budget = Budget(operation);

        long start = StreamSource.Position(source);
        byte[] buffer = StreamSource.ReadAhead(source, budget.Maximum, "payload", out int length);
        try
        {
            object? result = Decode(
                buffer.AsSpan(0, length), budget, declaredType, existingInstance, operation, out long consumed);

            StreamSource.Seek(source, start + consumed);
            return result;
        }
        finally
        {
            StreamSource.Return(buffer);
        }
    }

    public object? Read(
        ReadOnlySpan<byte> source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation)
    {
        var budget = Budget(operation);

        return Decode(
            source[..(int)Math.Min(source.Length, budget.Maximum)],
            budget, declaredType, existingInstance, operation, out _);
    }

    private static object? Decode(
        ReadOnlySpan<byte> bytes,
        WireBudget budget,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation,
        out long consumed)
    {
        var payloadOperation = WithoutReferences(operation);
        var reader = new WireReader(bytes, payloadOperation, budget);
        using var engine = new GraphReader(payloadOperation);

        object? result = existingInstance is null
            ? engine.ReadValue(ref reader, declaredType)
            : engine.ReadInto(ref reader, existingInstance, declaredType);

        consumed = reader.Consumed;
        return result;
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
