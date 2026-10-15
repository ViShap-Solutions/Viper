using System.Buffers;
using MemoryPack;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// MemoryPack 1.x with its generated formatters (<c>[MemoryPackable]</c>), the library's only mode. The
/// keyed models of DATA-13 and of the evolution probes are <c>GenerateType.VersionTolerant</c>, as Viper's
/// are <c>[BinaryContract]</c>: version tolerance is a property of the model in both libraries.
/// </summary>
internal sealed class MemoryPackAdapter()
    : MarketAdapter("MemoryPack", typeof(MemoryPackSerializer).Assembly, "default", ModelFamily.MemoryPack, Tier.T0 | Tier.T2),
      IBufferWriterSerializer, ISpanDeserializer
{
    internal override Rationale Rationale => new(
        "Source generator: [MemoryPackable] partial classes; [MemoryPackable(GenerateType.VersionTolerant)] with [MemoryPackOrder] on the keyed models (README, \"Version tolerant\")",
        "MemoryPackSerializerOptions.Default — UTF-8 strings",
        "No compression: MemoryPack.Compression's BrotliCompressor wraps the buffer outside the format, and is not a phase a reader can find in the payload",
        "StringEncoding.Utf16 writes strings without transcoding and is faster for string-heavy data at a larger size; the README keeps UTF-8 as the default, and the comparison uses the default");

    internal override IReadOnlyList<Limitation> Limitations =>
    [
        new("DATA-20", ".Big.", "BigInteger is read back as zero by version 1.21.4 on net10.0, reproduced outside this harness"),
    ];

    public override byte[] Serialize<T>(T value) => MemoryPackSerializer.Serialize(value);

    public override T? Deserialize<T>(byte[] payload) where T : default => MemoryPackSerializer.Deserialize<T>(payload);

    /// <summary>
    /// MemoryPack reads and writes streams only asynchronously; over a <see cref="MemoryStream"/> the call
    /// completes synchronously, and its result is taken where it completed.
    /// </summary>
    public override void Serialize<T>(Stream destination, T value) =>
        Completed.Wait(MemoryPackSerializer.SerializeAsync(destination, value));

    public override T? Deserialize<T>(Stream source) where T : default =>
        Completed.Wait(MemoryPackSerializer.DeserializeAsync<T>(source));

    public void Serialize<T>(IBufferWriter<byte> destination, T value) => MemoryPackSerializer.Serialize(destination, value);

    public T? Deserialize<T>(ReadOnlySequence<byte> payload) => MemoryPackSerializer.Deserialize<T>(payload);

    public T? Deserialize<T>(ReadOnlySpan<byte> payload) => MemoryPackSerializer.Deserialize<T>(payload);

    internal override ProbeOutcome SelfDescription() => ProbeOutcome.Unsupported(
        "no reader walks a payload without its type: the members follow one another in declaration order with no tag, and nothing of the payload's production is described");

    internal override object? Populate<T>(byte[] payload, T target) where T : class
    {
        T? value = target;
        MemoryPackSerializer.Deserialize(payload, ref value);
        return value;
    }
}
