using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// The generator profile — the steady state of a generated contract beside the reflected one, on the
/// same value and the same bytes (WL-01, WL-04, WL-08). Positional and keyed, few members and many, one
/// object and a batch of thousands, a union batch, each with reference framing off and on.
/// </summary>
/// <remarks>
/// The reflected and the generated profile of a pair differ in where the contract comes from and in
/// nothing else, so a difference between them is the contract source. Before any timing the setup checks
/// that the pair round-trips and that both profiles write the same bytes; a pair that does not is
/// refused rather than timed.
/// <para>
/// The wide models are seeded as SCALE-04 seeds them, so the B-P0 cells of the five-member rows and of
/// the keyed two-hundred-member row are a cross-check against <see cref="MemberCountScalingBenchmarks"/>. First use and cold
/// start are the process-level half of the profile: <see cref="FirstUseRunner"/> and
/// <see cref="ColdStartRunner"/>.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class GeneratorProfileBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<ViperProfile> Profiles =>
    [
        ViperProfile.Default,
        ViperProfile.Generated,
        ViperProfile.PreserveReferences,
        ViperProfile.GeneratedReferences,
    ];

    public static IEnumerable<Dataset> Datasets => GeneratorCases.All;

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;

        GeneratorCases.Verify(Profile, _dataset);

        _payload = _dataset.Serialize(_adapter);
    }

    [Benchmark(Description = "WL-01 serialize → byte[]")]
    public byte[] Serialize() => _dataset.Serialize(_adapter);

    [Benchmark(Description = "WL-04 deserialize ← byte[]")]
    public object? Deserialize() => _dataset.Deserialize(_adapter, _payload);
}

/// <summary>
/// The values of the generator profile: datasets of the corpus where one has the shape, and the
/// member-count models of SCALE-04 where none does.
/// </summary>
internal static class GeneratorCases
{
    internal static IReadOnlyList<Dataset> All { get; } =
    [
        Corpus.Find("DATA-01"),
        new WideDataset<WidePositional005>("SCALE-04/p5", "WidePositional005", 5),
        Corpus.Find("DATA-18"),
        new WideDataset<WideKeyed005>("SCALE-04/k5", "WideKeyed005", 5),
        new WideDataset<WideKeyed200>("SCALE-04/k200", "WideKeyed200", 200),
        Corpus.Find("DATA-02"),
        Corpus.Find("DATA-04"),
        Corpus.Find("DATA-13"),
        Corpus.Find("DATA-12"),
    ];

    /// <summary>
    /// Refuses a pair that does not round-trip, and a generated profile whose bytes differ from its
    /// reflected twin's, so no cell of the suite times an unverified pair (§7.6).
    /// </summary>
    internal static void Verify(ViperProfile profile, Dataset dataset)
    {
        var adapter = new ViperAdapter(profile);
        var result = RoundTripVerifier.Verify(adapter, adapter, dataset, ViperProfiles.PreservesReferences(profile));

        if (result.State is not (VerificationState.Supported or VerificationState.Partial))
        {
            throw new InvalidOperationException(
                $"{adapter.Name} on {dataset.Id} did not verify ({result.State}): {result.Detail}");
        }

        if (ViperProfiles.ReflectedTwin(profile) is { } twin)
        {
            var reflected = dataset.Serialize(new ViperAdapter(twin));

            if (!dataset.Serialize(adapter).AsSpan().SequenceEqual(reflected))
            {
                throw new InvalidOperationException(
                    $"{adapter.Name} and {ViperProfiles.PlanId(twin)} write different bytes for {dataset.Id}.");
            }
        }
    }

    /// <summary>One member-count model of SCALE-04, populated from the seed SCALE-04 uses.</summary>
    private sealed class WideDataset<T>(string id, string name, int members) : Dataset<T>
        where T : notnull, new()
    {
        internal override string Id => id;

        internal override string Name => name;

        internal override string Purpose => "Member-plan cost of one layout at one member count";

        protected override T Build() =>
            WideModels.Populate(new T(), new DeterministicRandom(0x0000_5004UL + (ulong)members));
    }
}
