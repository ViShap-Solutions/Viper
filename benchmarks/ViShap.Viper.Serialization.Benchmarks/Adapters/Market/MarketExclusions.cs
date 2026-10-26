namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>A library the roster of §5.2 does not measure, and why.</summary>
/// <param name="Library">The library as the plan names it.</param>
/// <param name="Version">The version the plan names, or empty when it names none.</param>
/// <param name="Reason">The plan's sentence, verbatim.</param>
internal sealed record Unmeasured(string Library, string Version, string Reason);

/// <summary>
/// The libraries of Benchmark-Plan §5.3 and §5.4, which a Track B run writes into its directory so that
/// its report says what was not measured and why (RST-04, RST-06) without reading anything outside it.
/// The text is the plan's; a change there is copied here in the same change.
/// </summary>
internal static class MarketExclusions
{
    /// <summary>§5.3 — measured where it adds information, never gating; none is run in this release.</summary>
    internal static IReadOnlyList<Unmeasured> SecondCircle { get; } =
    [
        new("Newtonsoft.Json", "13.0.4",
            "The honest \"it handles everything\" reference in T3: `$id`/`$ref` and type handling. Slow by construction; included so graph fidelity is not measured only among fast libraries"),
        new("Google.Protobuf", "3.33.0",
            "The canonical Protocol Buffers implementation. A different workflow — `.proto` and generated code rather than POCOs — so it is a wire-size and throughput reference on the flat datasets only"),
        new("Hyperion", "0.12.2",
            "Cycles, polymorphism and unattributed POCOs, the classic Akka.NET answer to T3. Fails criterion 2 today, kept as a datapoint because T3 has few members"),
        new("DataContractSerializer", "in-box",
            "The in-box reference-preserving serializer (`preserveObjectReferences`). Second circle because a second text format adds format cost rather than information"),
    ];

    /// <summary>The sentence a report prints for a second-circle library this harness has no adapter for.</summary>
    internal const string SecondCircleNotRun = "Not run for this release: the second circle gates nothing, and no adapter exists for it yet";

    /// <summary>§5.4 — excluded, each with the criterion it fails.</summary>
    internal static IReadOnlyList<Unmeasured> Excluded { get; } =
    [
        new("ZeroFormatter", string.Empty,
            "Fails criteria 2 and 3. Last published 2018-11-16 and superseded by its own author's MessagePack-CSharp and MemoryPack. Its deserialization is a lazy view: `Deserialize` returns almost immediately and the cost arrives when a property is touched, so a timed `Deserialize` would flatter it against every eager serializer here. The previous plan removed it for adapter trouble; these two are the real reasons"),
        new("Ceras", string.Empty,
            "Fails criterion 2. Last published 2019-08-23. Feature-wise the closest peer of its era — references, cycles, version tolerance — which is why its absence is recorded rather than passed over"),
        new("BinaryFormatter", string.Empty,
            "Cannot run. Removed from the runtime in .NET 9 and unsafe by design; there is no configuration in which it is a legitimate choice on `net10.0`"),
        new("FlatSharp / FlatBuffers, Cap'n Proto", string.Empty,
            "Fails criterion 3. Random-access zero-copy readers: \"deserialize\" is a pointer cast, and comparing it with an eager graph build measures a difference in programming model, not in speed"),
        new("XmlSerializer", string.Empty,
            "Fails criterion 3 for this corpus rather than in general: it cannot express dictionaries, interfaces, cycles or shared identity, so most of §9 would be `Unsupported`. `DataContractSerializer` covers the in-box XML answer in §5.3 with more of the corpus expressible"),
        new("Utf8Json, SpanJson, Jil", string.Empty,
            "Fail criterion 2, or serve a niche `System.Text.Json` now occupies"),
        new("Apache Avro (Chr.Avro), Apache Thrift, Bond", string.Empty,
            "Fail criterion 1 for this comparison: schema-registry and IDL-first ecosystems, chosen for interoperability with a platform rather than as a .NET serializer"),
    ];
}
