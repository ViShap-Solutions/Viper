using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper.Serialization.Benchmarks.Adapters;

namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>
/// One entry of the corpus of Benchmark-Plan §9. A dataset owns its value, knows its own static type,
/// and is the only place that decides which generic overload an adapter is called through.
/// </summary>
public abstract class Dataset
{
    /// <summary>The plan's identifier, as it appears in a published cell.</summary>
    internal abstract string Id { get; }

    internal abstract string Name { get; }

    /// <summary>What the dataset exists to expose, one line, as §9 states it.</summary>
    internal abstract string Purpose { get; }

    /// <summary>The static type the adapters are called with. Never <see cref="object"/>.</summary>
    internal abstract Type ValueType { get; }

    internal abstract object BoxedValue { get; }

    /// <summary>
    /// The value contains a cycle, so it cannot travel at all without reference framing (§16). A
    /// profile that does not preserve references is <c>Unsupported</c> for it, never <c>Failed</c>.
    /// </summary>
    internal virtual bool RequiresReferences => false;

    /// <summary>
    /// The value shares instances, so a profile without reference framing restores equal contents and
    /// loses identity. Such a pair is <c>Partial</c>, with the difference named.
    /// </summary>
    internal virtual bool IdentitySensitive => false;

    /// <summary>
    /// Checks the identity the value depends on, where it has one. Returns the failure, or
    /// <see langword="null"/> when identity survived.
    /// </summary>
    internal virtual string? VerifyIdentity(object restored) => null;

    internal abstract byte[] Serialize(IBufferedSerializer serializer);

    internal abstract object? Deserialize(IBufferedSerializer serializer, byte[] payload);

    internal abstract void SerializeTo(IStreamingSerializer serializer, Stream destination);

    internal abstract object? DeserializeFrom(IStreamingSerializer serializer, Stream source);

    internal abstract void SerializeInto(IBufferWriterSerializer serializer, IBufferWriter<byte> destination);

    internal abstract object? DeserializeSpan(IBufferWriterSerializer serializer, ReadOnlySpan<byte> payload);

    internal abstract object? DeserializeSequence(IBufferWriterSerializer serializer, ReadOnlySequence<byte> payload);

    /// <summary>Writes the value into a pooled payload and returns its length, disposing the payload.</summary>
    internal abstract int SerializePooled(ViperAdapter serializer);

    internal abstract ValueTask SerializeAsync(ViperAdapter serializer, Stream destination);

    internal abstract ValueTask SerializeAsync(ViperAdapter serializer, PipeWriter destination);

    internal abstract ValueTask<object?> DeserializeAsync(ViperAdapter serializer, Stream source);

    internal abstract ValueTask<object?> DeserializeAsync(ViperAdapter serializer, PipeReader source);

    /// <summary>Reads every frame of <paramref name="source"/> and returns how many there were.</summary>
    internal abstract ValueTask<int> DeserializeFrames(ViperAdapter serializer, PipeReader source);

    /// <summary>Reads every frame of <paramref name="source"/> and returns how many there were.</summary>
    internal abstract ValueTask<int> DeserializeFrames(ViperAdapter serializer, Stream source);

    public override string ToString() => $"{Id} {Name}";
}

/// <summary>A dataset of a known static type, built once from a fixed seed.</summary>
public abstract class Dataset<T> : Dataset
{
    private T? _value;
    private bool _built;

    internal T Value
    {
        get
        {
            if (!_built)
            {
                _value = Build();
                _built = true;
            }

            return _value!;
        }
    }

    internal override Type ValueType => typeof(T);

    internal override object BoxedValue => Value!;

    /// <summary>Builds the value deterministically. Called once, outside every timed region.</summary>
    protected abstract T Build();

    internal override byte[] Serialize(IBufferedSerializer serializer) => serializer.Serialize(Value);

    internal override object? Deserialize(IBufferedSerializer serializer, byte[] payload) =>
        serializer.Deserialize<T>(payload);

    internal override void SerializeTo(IStreamingSerializer serializer, Stream destination) =>
        serializer.Serialize(destination, Value);

    internal override object? DeserializeFrom(IStreamingSerializer serializer, Stream source) =>
        serializer.Deserialize<T>(source);

    internal override void SerializeInto(IBufferWriterSerializer serializer, IBufferWriter<byte> destination) =>
        serializer.Serialize(destination, Value);

    internal override object? DeserializeSpan(IBufferWriterSerializer serializer, ReadOnlySpan<byte> payload) =>
        serializer.Deserialize<T>(payload);

    internal override object? DeserializeSequence(IBufferWriterSerializer serializer, ReadOnlySequence<byte> payload) =>
        serializer.Deserialize<T>(payload);

    internal override int SerializePooled(ViperAdapter serializer)
    {
        using var payload = serializer.SerializePooled(Value);
        return payload.Memory.Length;
    }

    internal override ValueTask SerializeAsync(ViperAdapter serializer, Stream destination) =>
        serializer.SerializeAsync(destination, Value);

    internal override ValueTask SerializeAsync(ViperAdapter serializer, PipeWriter destination) =>
        serializer.SerializeAsync(destination, Value);

    internal override async ValueTask<object?> DeserializeAsync(ViperAdapter serializer, Stream source) =>
        await serializer.DeserializeAsync<T>(source).ConfigureAwait(false);

    internal override async ValueTask<object?> DeserializeAsync(ViperAdapter serializer, PipeReader source) =>
        await serializer.DeserializeAsync<T>(source).ConfigureAwait(false);

    internal override async ValueTask<int> DeserializeFrames(ViperAdapter serializer, PipeReader source)
    {
        int frames = 0;
        await foreach (var _ in serializer.DeserializeFrames<T>(source).ConfigureAwait(false))
            frames++;

        return frames;
    }

    internal override async ValueTask<int> DeserializeFrames(ViperAdapter serializer, Stream source)
    {
        int frames = 0;
        await foreach (var _ in serializer.DeserializeFrames<T>(source).ConfigureAwait(false))
            frames++;

        return frames;
    }
}
