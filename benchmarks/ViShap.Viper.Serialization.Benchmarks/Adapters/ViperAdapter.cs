using System.Buffers;
using System.IO.Pipelines;
using ViShap.Viper.Serialization.Benchmarks.Config;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters;

/// <summary>
/// Viper under one configuration profile. The serializer is built once, outside every timed region,
/// exactly as every other adapter's is.
/// </summary>
/// <remarks>
/// Beyond the families every adapter may share, it exposes the entry points only Viper has — the pooled
/// payload, the awaited stream and pipe, and the stream of frames — for the Viper-only workloads of the
/// plan's §10.
/// </remarks>
internal sealed class ViperAdapter : IBufferedSerializer, IStreamingSerializer, IBufferWriterSerializer
{
    private readonly BinarySerializer _serializer;

    internal ViperAdapter(ViperProfile profile)
    {
        Profile = profile;
        Name = $"Viper {ViperProfiles.PlanId(profile)}";
        _serializer = new BinarySerializer(ViperProfiles.Options(profile));
    }

    internal ViperProfile Profile { get; }

    public string Name { get; }

    public byte[] Serialize<T>(T value) => _serializer.Serialize(value);

    public T? Deserialize<T>(byte[] payload) => _serializer.Deserialize<T>(payload);

    public void Serialize<T>(Stream destination, T value) => _serializer.Serialize(destination, value);

    public T? Deserialize<T>(Stream source) => _serializer.Deserialize<T>(source);

    public void Serialize<T>(IBufferWriter<byte> destination, T value) => _serializer.Serialize(destination, value);

    public T? Deserialize<T>(ReadOnlySpan<byte> payload) => _serializer.Deserialize<T>(payload);

    public T? Deserialize<T>(ReadOnlySequence<byte> payload) => _serializer.Deserialize<T>(payload);

    internal PooledPayload SerializePooled<T>(T value) => _serializer.SerializePooled(value);

    internal ValueTask SerializeAsync<T>(Stream destination, T value) => _serializer.SerializeAsync(destination, value);

    internal ValueTask SerializeAsync<T>(PipeWriter destination, T value) => _serializer.SerializeAsync(destination, value);

    internal ValueTask<T?> DeserializeAsync<T>(Stream source) => _serializer.DeserializeAsync<T>(source);

    internal ValueTask<T?> DeserializeAsync<T>(PipeReader source) => _serializer.DeserializeAsync<T>(source);

    internal IAsyncEnumerable<T?> DeserializeFrames<T>(PipeReader source) => _serializer.DeserializeAsyncEnumerable<T>(source);

    internal IAsyncEnumerable<T?> DeserializeFrames<T>(Stream source) => _serializer.DeserializeAsyncEnumerable<T>(source);

}

/// <summary>
/// The floor. It does the harness's work and none of a serializer's, so any cell close to it is a
/// harness artifact rather than a fast serializer (Benchmark-Plan VAL-07).
/// </summary>
internal sealed class EmptyAdapter : IBufferedSerializer, IStreamingSerializer
{
    private static readonly byte[] One = [0];

    public string Name => "Empty (harness floor)";

    public byte[] Serialize<T>(T value) => One;

    public T? Deserialize<T>(byte[] payload) => default;

    public void Serialize<T>(Stream destination, T value) => destination.Write(One, 0, 1);

    public T? Deserialize<T>(Stream source) => default;
}
