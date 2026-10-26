using System.Buffers;
using Nerdbank.MessagePack;
using PolyType;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Models.NerdbankVariant;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// Nerdbank.MessagePack 1.x over the shapes PolyType's source generator writes for every root
/// (<c>[GenerateShapeFor&lt;T&gt;]</c>), in the three configurations its documentation describes for
/// the three jobs: compact and closed-world, the version-tolerant default, and reference preservation.
/// </summary>
internal sealed class NerdbankAdapter : MarketAdapter, IBufferWriterSerializer
{
    private readonly MessagePackSerializer _serializer;
    private readonly string _options;

    private NerdbankAdapter(string configuration, Tier intended, string options, MessagePackSerializer serializer)
        : base("Nerdbank.MessagePack", typeof(MessagePackSerializer).Assembly, configuration, ModelFamily.NerdbankMessagePack, intended)
    {
        _options = options;
        _serializer = serializer;
    }

    internal static NerdbankAdapter Compact() =>
        new("PerfOverSchemaStability", Tier.T0, "new MessagePackSerializer { PerfOverSchemaStability = true }",
            new MessagePackSerializer { PerfOverSchemaStability = true });

    internal static NerdbankAdapter Default() =>
        new("default", Tier.T1 | Tier.T2, "new MessagePackSerializer()", new MessagePackSerializer());

    internal static NerdbankAdapter References() =>
        new("AllowCycles", Tier.T3, "new MessagePackSerializer { PreserveReferences = ReferencePreservationMode.AllowCycles }",
            new MessagePackSerializer { PreserveReferences = ReferencePreservationMode.AllowCycles });

    internal override Rationale Rationale => new(
        "Source generator: PolyType shapes for every root through [GenerateShapeFor<T>] on one witness class, passed as ITypeShape<T> to each call (docs, \"Getting started\", \"Type shapes\")",
        $"{_options}; [DerivedTypeShape] with integer tags for the union arms; [DefaultValue(\"\")] on string members initialized to empty, as analyzer NBMsgPack110 asks",
        "No compression: version 1.4.82 ships no compression phase; SerializeDefaultValues and DeserializeDefaultValues keep their defaults",
        "PerfOverSchemaStability gives up schema stability for speed, so it is the closed-world configuration only; the tolerant and reference-preserving tiers use the default");

    internal override IReadOnlyList<Limitation> Limitations =>
    [
        new("DATA-06", "Exceeded maximum depth of object graph", "the default maximum object-graph depth is 64, below DATA-06/200 and DATA-06/511"),
        new("DATA-20", ".Local:", "a DateTime of kind Local is written as a UTC timestamp and read back as UTC, so its clock time moves by the machine's offset"),
    ];

    public override byte[] Serialize<T>(T value) => _serializer.Serialize(value, Shape<T>.Value);

    public override T? Deserialize<T>(byte[] payload) where T : default => _serializer.Deserialize(payload, Shape<T>.Value);

    public override void Serialize<T>(Stream destination, T value) => _serializer.Serialize(destination, value, Shape<T>.Value);

    public override T? Deserialize<T>(Stream source) where T : default => _serializer.Deserialize(source, Shape<T>.Value);

    public void Serialize<T>(IBufferWriter<byte> destination, T value) => _serializer.Serialize(destination, value, Shape<T>.Value);

    public T? Deserialize<T>(ReadOnlySequence<byte> payload) => _serializer.Deserialize(payload, Shape<T>.Value);

    internal override ProbeOutcome SelfDescription()
    {
        var payload = Serialize(new Models.NerdbankVariant.ProbeRecord { A = 7, B = "self-described", C = 2.5 });
        var reader = new MessagePackReader(payload);
        var primitives = _serializer.DeserializePrimitives(ref reader);

        return Probes.ValuesOnly($"every value with its MessagePack type ({primitives?.GetType().Name ?? "nil"} at the root)", payload.Length);
    }

    /// <summary>The generated shape of one root, looked up once for the process.</summary>
    private static class Shape<T>
    {
        internal static readonly ITypeShape<T> Value =
            NerdbankShapes.GeneratedTypeShapeProvider.GetTypeShape(typeof(T)) as ITypeShape<T>
            ?? throw new InvalidOperationException($"NerdbankShapes generates no shape for '{typeof(T)}'.");
    }
}
