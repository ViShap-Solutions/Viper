using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Models.SystemTextJsonVariant;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// System.Text.Json, in-box, through a source-generated <see cref="JsonSerializerContext"/> for every
/// root; the reference-preserving tier through a second context generated with
/// <c>ReferenceHandler.Preserve</c>. Not a binary format: it shows what the default choice costs.
/// </summary>
internal sealed class SystemTextJsonAdapter : MarketAdapter, IBufferWriterSerializer, ISpanDeserializer
{
    private readonly JsonSerializerContext _context;
    private readonly Utf8JsonWriter _writer = new(new ArrayBufferWriter<byte>(1));

    private SystemTextJsonAdapter(string configuration, Tier intended, JsonSerializerContext context)
        : base("System.Text.Json", typeof(JsonSerializer).Assembly, configuration, ModelFamily.SystemTextJson, intended)
    {
        _context = context;
    }

    internal static SystemTextJsonAdapter Default() => new("default", Tier.T1 | Tier.T2, JsonModels.Default);

    internal static SystemTextJsonAdapter Preserve() => new("Preserve", Tier.T3, JsonPreservingModels.Default);

    internal override Rationale Rationale => new(
        "Source generator: a JsonSerializerContext with [JsonSerializable] for every root, each call given the root's JsonTypeInfo<T> (\"How to use source generation in System.Text.Json\")",
        _context is JsonPreservingModels
            ? "[JsonSourceGenerationOptions(ReferenceHandler = JsonKnownReferenceHandler.Preserve)]; [JsonPolymorphic] and [JsonDerivedType] with integer discriminators"
            : "Default JsonSourceGenerationOptions; [JsonPolymorphic] and [JsonDerivedType] with integer discriminators",
        "No indentation, no relaxed escaping, no IncludeFields: the defaults a consumer gets",
        "JsonSourceGenerationMode.Serialization (the fast-path writer) is not used: it cannot read, and the comparison times both directions with one context");

    internal override IReadOnlyList<Limitation> Limitations =>
    [
        new("DATA-06", "maximum allowed depth of 64", "the default MaxDepth is 64, below DATA-06/200 and DATA-06/511"),
        new("DATA-19", "Int32[,]", "multidimensional arrays are not supported"),
        new("DATA-20", ".Big.", "BigInteger has no converter: it is written as an object of its read-only properties and read back as zero"),
    ];

    public override byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Info<T>());

    public override T? Deserialize<T>(byte[] payload) where T : default => JsonSerializer.Deserialize((ReadOnlySpan<byte>)payload, Info<T>());

    public override void Serialize<T>(Stream destination, T value) => JsonSerializer.Serialize(destination, value, Info<T>());

    public override T? Deserialize<T>(Stream source) where T : default => JsonSerializer.Deserialize(source, Info<T>());

    /// <summary>
    /// Writes through one <see cref="Utf8JsonWriter"/> reset onto each destination, as the documentation
    /// shows for writing to a buffer writer repeatedly.
    /// </summary>
    public void Serialize<T>(IBufferWriter<byte> destination, T value)
    {
        _writer.Reset(destination);
        JsonSerializer.Serialize(_writer, value, Info<T>());
        _writer.Flush();
    }

    public T? Deserialize<T>(ReadOnlySequence<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        return JsonSerializer.Deserialize(ref reader, Info<T>());
    }

    public T? Deserialize<T>(ReadOnlySpan<byte> payload) => JsonSerializer.Deserialize(payload, Info<T>());

    internal override ProbeOutcome SelfDescription()
    {
        var payload = Serialize(new Models.SystemTextJsonVariant.ProbeRecord { A = 7, B = "self-described", C = 2.5 });
        using var document = JsonDocument.Parse(payload);

        return Probes.ValuesOnly($"{document.RootElement.EnumerateObject().Count()} members, each with its name and JSON value type", payload.Length);
    }

    private JsonTypeInfo<T> Info<T>() =>
        _context is JsonPreservingModels ? TypeInfo<T>.Preserving : TypeInfo<T>.Plain;

    /// <summary>The generated metadata of one root in both contexts, looked up once for the process.</summary>
    private static class TypeInfo<T>
    {
        internal static readonly JsonTypeInfo<T> Plain = Find(JsonModels.Default);

        internal static readonly JsonTypeInfo<T> Preserving = Find(JsonPreservingModels.Default);

        private static JsonTypeInfo<T> Find(JsonSerializerContext context) =>
            context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
            ?? throw new InvalidOperationException($"{context.GetType().Name} generates no metadata for '{typeof(T)}'.");
    }
}
