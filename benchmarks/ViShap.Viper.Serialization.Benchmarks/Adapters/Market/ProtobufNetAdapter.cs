using System.Buffers;
using ProtoBuf;
using ProtoBuf.Meta;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// protobuf-net 3.x: <c>[ProtoContract]</c> models, every one added to a <see cref="RuntimeTypeModel"/>
/// that is compiled once before the first operation (FAIR-14).
/// </summary>
internal sealed class ProtobufNetAdapter : MarketAdapter, IBufferWriterSerializer, ISpanDeserializer
{
    private readonly TypeModel _model;

    internal ProtobufNetAdapter()
        : base("protobuf-net", typeof(Serializer).Assembly, "compiled", ModelFamily.ProtobufNet, Tier.T0 | Tier.T2)
    {
        var model = RuntimeTypeModel.Create("viper-benchmarks");
        var types = typeof(ProtobufNetAdapter).Assembly.GetTypes()
            .Where(type => type.Namespace == ModelFamilies.Namespace(ModelFamily.ProtobufNet)
                && type.IsDefined(typeof(ProtoContractAttribute), inherit: false))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var type in types)
            model.Add(type, applyDefaultBehaviour: true);

        var refused = new List<string>();
        foreach (var type in types)
        {
            try
            {
                model[type].CompileInPlace();
            }
            catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
            {
                refused.Add($"{type.Name} ({exception.Message})");
            }
        }

        Refused = refused;
        _model = model;
    }

    /// <summary>The model types protobuf-net refused to compile, each with its reason.</summary>
    internal IReadOnlyList<string> Refused { get; }

    internal override Rationale Rationale => new(
        "Compiled model: a RuntimeTypeModel with every [ProtoContract] added and each compiled with CompileInPlace() before the first operation (\"Getting started\", \"Precompiling\")",
        "Default model behaviour; [ProtoMember(n)] numbered as the schema's keys, [ProtoInclude] for the union arms",
        "No member is marked AsReference: the schema is plain Protocol Buffers, and the probes of §7.5 report what the model does with a shared instance and a cycle",
        "Compile() into one TypeModel is the alternative; it compiles every type together, and one type the format cannot express (CollectionZoo's int[,]) refuses the whole model, so each type is compiled on its own instead");

    internal override IReadOnlyList<Limitation> Limitations =>
    [
        new("DATA-19", "Int32[,]", "Protocol Buffers has no multidimensional array"),
        new("DATA-20", "No serializer defined for type: System.DateTimeOffset", "the model has no serializer for DateTimeOffset, nor for Int128, BigInteger, Half, Complex or the System.Numerics vectors"),
    ];

    public override byte[] Serialize<T>(T value)
    {
        using var buffer = new MemoryStream();
        _model.Serialize(buffer, value);
        return buffer.ToArray();
    }

    public override T? Deserialize<T>(byte[] payload) where T : default => _model.Deserialize<T>((ReadOnlySpan<byte>)payload);

    public override void Serialize<T>(Stream destination, T value) => _model.Serialize(destination, value);

    public override T? Deserialize<T>(Stream source) where T : default => _model.Deserialize<T>(source);

    public void Serialize<T>(IBufferWriter<byte> destination, T value) => _model.Serialize(destination, value);

    public T? Deserialize<T>(ReadOnlySequence<byte> payload) => _model.Deserialize<T>(payload);

    public T? Deserialize<T>(ReadOnlySpan<byte> payload) => _model.Deserialize<T>(payload);

    internal override ProbeOutcome SelfDescription()
    {
        var payload = Serialize(new Models.ProtobufNetVariant.ProbeRecord { A = 7, B = "self-described", C = 2.5 });
        var state = ProtoReader.State.Create(new ReadOnlyMemory<byte>(payload), _model, null);
        var fields = 0;

        try
        {
            while (state.ReadFieldHeader() > 0)
            {
                state.SkipField();
                fields++;
            }
        }
        finally
        {
            state.Dispose();
        }

        return Probes.ValuesOnly($"{fields} fields, each with its number and wire type and no name", payload.Length);
    }

    internal override object? Populate<T>(byte[] payload, T target) where T : class =>
        _model.Deserialize((ReadOnlySpan<byte>)payload, target);
}
