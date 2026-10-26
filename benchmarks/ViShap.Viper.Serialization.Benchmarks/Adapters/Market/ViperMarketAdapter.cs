using System.Buffers;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// Viper in Track B: a generated context with the type information of every root, in the configuration
/// each tier asks for (Benchmark-Plan §6, TIER-03). Every call takes the root's
/// <see cref="BinaryTypeInfo{T}"/>, resolved once per type outside the timed region, so nothing is
/// described by reflection and a type the context does not hold is refused.
/// </summary>
internal sealed class ViperMarketAdapter : MarketAdapter, IBufferWriterSerializer, ISpanDeserializer
{
    private readonly BinarySerializer _serializer;
    private readonly string _options;
    private readonly Func<BinarySerializerOptionsBuilder, BinarySerializerOptionsBuilder> _configure;

    private ViperMarketAdapter(string configuration, Tier intended, string options, Func<BinarySerializerOptionsBuilder, BinarySerializerOptionsBuilder> configure)
        : base("ViShap.Viper", typeof(BinarySerializer).Assembly, configuration, ModelFamily.Viper, intended)
    {
        _options = options;
        _configure = configure;
        _serializer = new BinarySerializer(configure(BinarySerializerOptions.Configure()
            .WithContracts(MarketContracts.Default)
            .RequireGeneratedContracts()).Build());
    }

    /// <summary>T0 — the compact codec: a bare payload, the transport supplies the context (B-P7).</summary>
    internal static ViperMarketAdapter Compact() =>
        new("V0", Tier.T0, "WithVersion(0).AllowV0Fallback()", builder => builder.WithVersion(0).AllowV0Fallback());

    /// <summary>T1 and T2 — the default V1 frame (B-P0); T2 over the keyed models of DATA-13.</summary>
    internal static ViperMarketAdapter Framed() =>
        new("V1", Tier.T1 | Tier.T2, "defaults", builder => builder);

    /// <summary>T3 — the V1 frame with reference framing (B-P1); unions travel as tags in every profile.</summary>
    internal static ViperMarketAdapter References() =>
        new("V1 references", Tier.T3, "PreserveReferences()", builder => builder.PreserveReferences());

    /// <summary>The compressed profile a library's own compression is compared with (FAIR-16, B-P3d).</summary>
    internal static ViperMarketAdapter Deflate() =>
        new("V1 Deflate", Tier.T1, "WithCompression(new DeflateCompression())", builder => builder.WithCompression(new DeflateCompression()))
        {
            Compressed = true,
        };

    internal override Rationale Rationale => new(
        "Source generator: a [BinaryContext] class listing every root, and each call given the root's BinaryTypeInfo<T> (docs/aot.md, docs/generator.md)",
        $"BinarySerializerOptions.Configure().WithContracts(MarketContracts.Default).RequireGeneratedContracts(), then {_options}; the shipping SerializationLimits.Default",
        "No limit is raised above SerializationLimits.Default (FAIR-18); RequireGeneratedContracts() refuses the reflection path rather than falling back to it",
        "None: the generated context with type information is the documented best mode; the reflected path is measured in Track A");

    public override byte[] Serialize<T>(T value) => _serializer.Serialize(value, TypeInfo<T>.Value);

    public override T? Deserialize<T>(byte[] payload) where T : default => _serializer.Deserialize(payload, TypeInfo<T>.Value);

    public override void Serialize<T>(Stream destination, T value) => _serializer.Serialize(destination, value, TypeInfo<T>.Value);

    public override T? Deserialize<T>(Stream source) where T : default => _serializer.Deserialize(source, TypeInfo<T>.Value);

    public void Serialize<T>(IBufferWriter<byte> destination, T value) => _serializer.Serialize(destination, value, TypeInfo<T>.Value);

    public T? Deserialize<T>(ReadOnlySequence<byte> payload) => _serializer.Deserialize(payload, TypeInfo<T>.Value);

    public T? Deserialize<T>(ReadOnlySpan<byte> payload) => _serializer.Deserialize(payload, TypeInfo<T>.Value);

    /// <summary>
    /// This configuration with Brotli and CRC-32 added writes; a serializer configured with nothing but
    /// the context reads.
    /// </summary>
    internal override ProbeOutcome SelfDescription()
    {
        var writer = new BinarySerializer(_configure(BinarySerializerOptions.Configure()
            .WithContracts(MarketContracts.Default)
            .WithCompression(new BrotliCompression())
            .WithChecksum(new Crc32Checksum())).Build());
        var reader = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(MarketContracts.Default).Build());

        var value = new ProbeRecord { A = 7, B = "self-described", C = 2.5 };
        var payload = writer.Serialize(value, TypeInfo<ProbeRecord>.Value);

        try
        {
            return reader.Deserialize(payload, TypeInfo<ProbeRecord>.Value) is { A: 7, B: "self-described", C: 2.5 }
                ? ProbeOutcome.Supported("Brotli and CRC-32 named in the header; a reader configured with nothing undid both")
                : ProbeOutcome.Unsupported("a reader configured with nothing did not restore the value");
        }
        catch (BinarySerializerException exception)
        {
            return ProbeOutcome.Unsupported($"a reader configured with nothing refused the payload: {exception.GetType().Name}: {exception.Message}");
        }
    }

    internal override object? Populate<T>(byte[] payload, T target) where T : class
    {
        _serializer.Populate(payload, target, TypeInfo<T>.Value);
        return target;
    }

    /// <summary>The type information of one root, created once for the process.</summary>
    private static class TypeInfo<T>
    {
        internal static readonly BinaryTypeInfo<T> Value = MarketContracts.Default.TryGetTypeInfo<T>()
            ?? throw new InvalidOperationException($"MarketContracts does not list '{typeof(T)}'.");
    }
}
