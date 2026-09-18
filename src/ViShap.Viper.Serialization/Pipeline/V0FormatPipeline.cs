namespace ViShap.Viper.Pipeline;

/// <summary>
/// The V0 envelope: a headerless positional payload, kept for legacy compatibility. It carries no
/// metadata, so it supports neither keyed contracts nor reference framing. Unlike V1 it may be
/// embedded in a larger stream, which is why it does not require the source to end with the payload.
/// </summary>
internal sealed class V0FormatPipeline : IFormatPipeline
{
    private const bool KeyedContractsSupported = false;

    public int Version => 0;

    public void Write<T>(Stream destination, T data, SerializationOperation operation)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var wire = new MeteredWriteStream(destination, operation.Limits.MaxWireBytes, "wire");
        var payload = new MeteredWriteStream(wire, operation.Limits.MaxPayloadBytes, "payload");
        var writer = new ValueWriter(payload, operation);

        new GraphWriter(writer, Positional(operation), KeyedContractsSupported).WriteRoot(data);

        writer.Flush();
    }

    public T? Read<T>(Stream source, SerializationOperation operation)
    {
        var reader = OpenReader(source, operation, out var positional);
        return new GraphReader(reader, positional, KeyedContractsSupported).ReadRoot<T>();
    }

    public T Read<T>(Stream source, T existingInstance, SerializationOperation operation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);

        var reader = OpenReader(source, operation, out var positional);
        return (T)new GraphReader(reader, positional, KeyedContractsSupported)
            .ReadInto(existingInstance, typeof(T));
    }

    private static ValueReader OpenReader(
        Stream source,
        SerializationOperation operation,
        out SerializationOperation positional)
    {
        ArgumentNullException.ThrowIfNull(source);

        positional = Positional(operation);

        // Both ceilings apply on the way in, exactly as they do on the way out.
        var wire = new MeteredReadStream(source, operation.Limits.MaxWireBytes, "wire");
        var payload = new MeteredReadStream(wire, operation.Limits.MaxPayloadBytes, "payload");
        return new ValueReader(payload, positional);
    }

    private static SerializationOperation Positional(SerializationOperation operation) =>
        operation.WithPreserveReferences(false);
}
