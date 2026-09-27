namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

/// <summary>A serializer measured through its buffered entry points.</summary>
internal interface IBufferedSerializer
{
    string Name { get; }

    byte[] Serialize<T>(T value);

    T? Deserialize<T>(byte[] payload);
}

/// <summary>A serializer measured through its stream entry points.</summary>
internal interface IStreamingSerializer
{
    string Name { get; }

    void Serialize<T>(Stream destination, T value);

    T? Deserialize<T>(Stream source);
}

/// <summary>
/// A serializer measured through its buffer entry points: it writes into a caller's
/// <see cref="System.Buffers.IBufferWriter{T}"/> and reads from a span or a sequence, which is how
/// serializers built for buffers are called (Benchmark-Plan FAIR-07). An adapter that has no such
/// entry point does not implement this, and its cells in the family are <c>Unsupported</c>.
/// </summary>
internal interface IBufferWriterSerializer
{
    string Name { get; }

    void Serialize<T>(System.Buffers.IBufferWriter<byte> destination, T value);

    T? Deserialize<T>(ReadOnlySpan<byte> payload);

    T? Deserialize<T>(System.Buffers.ReadOnlySequence<byte> payload);
}
