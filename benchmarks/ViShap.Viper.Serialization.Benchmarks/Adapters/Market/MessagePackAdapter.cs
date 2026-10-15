using System.Buffers;
using MessagePack;
using MessagePack.ImmutableCollection;
using MessagePack.Resolvers;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ProbeRecord = ViShap.Viper.Serialization.Benchmarks.Models.MessagePackVariant.ProbeRecord;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// MessagePack-CSharp 3.x: <c>[MessagePackObject]</c> models with integer keys, whose formatters the
/// library's source generator writes, resolved through the default options; measured with its LZ4
/// compression off and on (FAIR-16).
/// </summary>
internal sealed class MessagePackAdapter : MarketAdapter, IBufferWriterSerializer
{
    private static readonly MessagePackSerializerOptions Defaults = MessagePackSerializer.DefaultOptions.WithResolver(
        CompositeResolver.Create(ImmutableCollectionResolver.Instance, MessagePackSerializer.DefaultOptions.Resolver));

    private readonly MessagePackSerializerOptions _options;

    private MessagePackAdapter(string configuration, Tier intended, MessagePackSerializerOptions options)
        : base("MessagePack-CSharp", typeof(MessagePackSerializer).Assembly, configuration, ModelFamily.MessagePack, intended)
    {
        _options = options;
    }

    internal static MessagePackAdapter Default() => new("default", Tier.T0 | Tier.T2, Defaults);

    internal static MessagePackAdapter Lz4() =>
        new("Lz4BlockArray", Tier.T0 | Tier.T2, Defaults.WithCompression(MessagePackCompression.Lz4BlockArray))
        {
            Compressed = true,
        };

    internal override Rationale Rationale => new(
        "Source generator: [MessagePackObject] with [Key(int)], formatters written at build time and found through MessagePackSerializer.DefaultOptions (v3 \"AOT-ready source generation\")",
        Compressed
            ? "DefaultOptions, the ImmutableCollectionResolver of the first-party MessagePack.ImmutableCollection package before the default resolver, and WithCompression(MessagePackCompression.Lz4BlockArray), the mode the README recommends"
            : "DefaultOptions, with the ImmutableCollectionResolver of the first-party MessagePack.ImmutableCollection package before the default resolver, so the immutable collections of DATA-19 have formatters",
        "MessagePackSecurity.UntrustedData is not set: the README recommends it for untrusted input, where it adds collision-resistant hashing; the default is what a consumer gets",
        "[Key(string)] maps are more tolerant and larger; integer keys are the README's recommendation for performance");

    internal override IReadOnlyList<Limitation> Limitations =>
    [
        new("DATA-06", "maximum depth allowed of 500", "reading enforces a maximum object-graph depth of 500, below DATA-06/511"),
        new("DATA-20", ".Local:", "a DateTime of kind Local is written as a UTC timestamp and read back as UTC, so its clock time moves by the machine's offset"),
    ];

    public override byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, _options);

    public override T? Deserialize<T>(byte[] payload) where T : default => MessagePackSerializer.Deserialize<T>(payload, _options);

    public override void Serialize<T>(Stream destination, T value) => MessagePackSerializer.Serialize(destination, value, _options);

    public override T? Deserialize<T>(Stream source) where T : default => MessagePackSerializer.Deserialize<T>(source, _options);

    public void Serialize<T>(IBufferWriter<byte> destination, T value) => MessagePackSerializer.Serialize(destination, value, _options);

    public T? Deserialize<T>(ReadOnlySequence<byte> payload) => MessagePackSerializer.Deserialize<T>(payload, _options);

    internal override ProbeOutcome SelfDescription()
    {
        var value = new ProbeRecord { A = 7, B = "self-described", C = 2.5 };
        var plain = MessagePackSerializer.Serialize(value, Defaults);
        var json = MessagePackSerializer.ConvertToJson(plain, Defaults);

        var compressed = MessagePackSerializer.Serialize(value, Defaults.WithCompression(MessagePackCompression.Lz4BlockArray));
        string lz4;

        try
        {
            lz4 = MessagePackSerializer.Deserialize<ProbeRecord>(compressed, Defaults) is { A: 7, B: "self-described", C: 2.5 }
                ? "an LZ4 payload marks itself with an extension type, and a reader with no compression set decompresses it"
                : "a reader with no compression set misread an LZ4 payload";
        }
        catch (MessagePackSerializationException exception)
        {
            lz4 = $"a reader with no compression set refused an LZ4 payload ({exception.GetType().Name})";
        }

        return ProbeOutcome.Partial(
            $"a reader with no schema converted all {plain.Length} bytes to JSON ({json.Length} characters): every value carries its MessagePack type, integer keys stand for names; " +
            $"{lz4}; nothing else of the payload's production — length, checksum, version — is described");
    }
}
