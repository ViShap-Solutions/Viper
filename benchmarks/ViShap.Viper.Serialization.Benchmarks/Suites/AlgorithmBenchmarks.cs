using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// §15 and §16 — the algorithm phases, over the data shapes that decide what they cost: entropy for
/// compression, volume for the checksum and the cipher. The no-algorithm profile is measured on the
/// same datasets, so a phase's cost is a difference rather than an estimate (CMP-03).
/// </summary>
[MemoryDiagnoser]
public class AlgorithmBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<ViperProfile> Profiles =>
    [
        ViperProfile.Default,
        ViperProfile.Deflate,
        ViperProfile.Brotli,
        ViperProfile.Crc32,
        ViperProfile.Aes256Gcm,
        ViperProfile.ProtectedDeflate,
        ViperProfile.ProtectedBrotli,
    ];

    public static IEnumerable<Dataset> Datasets =>
    [
        Corpus.Find("DATA-08"),
        Corpus.Find("DATA-09"),
        Corpus.Find("DATA-14/blob"),
        Corpus.Find("DATA-16"),
        Corpus.Find("DATA-04"),
    ];

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;
        _payload = _dataset.Serialize(_adapter);
    }

    [Benchmark(Description = "phase write")]
    public byte[] Serialize() => _dataset.Serialize(_adapter);

    [Benchmark(Description = "phase read")]
    public object? Deserialize() => _dataset.Deserialize(_adapter, _payload);
}
