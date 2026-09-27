using System.Buffers;

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
/// leaves nothing in it. Nothing in the payload says where it ends: a reader finds the end by decoding
/// the root value, so bytes after it are read only by a caller who asks where the payload ended, and a
/// stream is read ahead and put back where the root ends, which needs a stream that can seek.
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

    /// <summary>
    /// A V0 payload declares no length: where it ends is known only once its root value has been
    /// decoded. A source that delivers bytes over time can therefore only read one ahead, within the
    /// budget, and give back what the root did not use.
    /// </summary>
    public FrameExtent Measure(
        ReadOnlySpan<byte> buffered,
        bool sourceEnded,
        long available,
        SerializationOperation operation) =>
        FrameExtent.Undeclared(Budget(operation).Maximum);

    public object? Read(
        ReadOnlySpan<byte> source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation,
        out long consumed)
    {
        var budget = Budget(operation);
        var payloadOperation = WithoutReferences(operation);
        var reader = new WireReader(
            source[..(int)Math.Min(source.Length, budget.Maximum)], payloadOperation, budget);

        return Decode(ref reader, declaredType, existingInstance, payloadOperation, out consumed);
    }

    public object? Read(
        ReadOnlySequence<byte> source,
        Type declaredType,
        object? existingInstance,
        SerializationOperation operation,
        out long consumed)
    {
        if (source.IsSingleSegment)
            return Read(source.FirstSpan, declaredType, existingInstance, operation, out consumed);

        var budget = Budget(operation);
        var payloadOperation = WithoutReferences(operation);
        var reader = new WireReader(
            source.Slice(0, Math.Min(source.Length, budget.Maximum)), payloadOperation, budget);

        return Decode(ref reader, declaredType, existingInstance, payloadOperation, out consumed);
    }

    private static object? Decode(
        ref WireReader reader,
        Type declaredType,
        object? existingInstance,
        SerializationOperation payloadOperation,
        out long consumed)
    {
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
