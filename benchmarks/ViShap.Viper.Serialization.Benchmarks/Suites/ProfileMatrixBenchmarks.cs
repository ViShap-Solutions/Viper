using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// B1 — Viper against Viper. Every configuration profile of §8 over the core corpus, through the
/// buffered entry points of §3.1 (WL-01, WL-04, WL-06).
/// </summary>
/// <remarks>
/// The serializer, the value and the payload are prepared in <see cref="Setup"/>; the timed methods
/// contain the operation and nothing else.
/// </remarks>
[MemoryDiagnoser]
public class ProfileMatrixBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<ViperProfile> Profiles => ViperProfiles.All;

    public static IEnumerable<Dataset> Datasets => Corpus.Core;

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

    [Benchmark(Description = "WL-01 serialize → byte[]")]
    public byte[] Serialize() => _dataset.Serialize(_adapter);

    [Benchmark(Description = "WL-04 deserialize ← byte[]")]
    public object? Deserialize() => _dataset.Deserialize(_adapter, _payload);

    [Benchmark(Description = "WL-06 round trip")]
    public object? RoundTrip() => _dataset.Deserialize(_adapter, _dataset.Serialize(_adapter));
}

/// <summary>
/// B1 — the same profiles through the stream entry points (WL-02, WL-05). The destination is reset
/// rather than reallocated, so the measurement is the write and not the buffer growth.
/// </summary>
[MemoryDiagnoser]
public class ProfileStreamBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private MemoryStream _destination = null!;
    private MemoryStream _source = null!;

    public static IEnumerable<ViperProfile> Profiles => ViperProfiles.All;

    public static IEnumerable<Dataset> Datasets => Corpus.Core;

    [ParamsSource(nameof(Profiles))]
    public ViperProfile Profile { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);
        _dataset = Data;

        var payload = _dataset.Serialize(_adapter);

        _destination = new MemoryStream(payload.Length);
        _source = new MemoryStream(payload, writable: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _destination.Dispose();
        _source.Dispose();
    }

    [Benchmark(Description = "WL-02 serialize → Stream")]
    public long SerializeToStream()
    {
        _destination.Position = 0;
        _dataset.SerializeTo(_adapter, _destination);
        return _destination.Position;
    }

    [Benchmark(Description = "WL-05 deserialize ← Stream")]
    public object? DeserializeFromStream()
    {
        _source.Position = 0;
        return _dataset.DeserializeFrom(_adapter, _source);
    }
}

/// <summary>
/// VAL-07 — the harness floor. An adapter that does none of a serializer's work, measured over the
/// same datasets through the same dispatch, so any cell close to it is a harness artifact.
/// </summary>
[MemoryDiagnoser]
public class HarnessFloorBenchmarks
{
    private readonly EmptyAdapter _adapter = new();
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<Dataset> Datasets => Corpus.Core;

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dataset = Data;
        _payload = _dataset.Serialize(_adapter);
    }

    [Benchmark(Description = "floor serialize")]
    public byte[] Serialize() => _dataset.Serialize(_adapter);

    [Benchmark(Description = "floor deserialize")]
    public object? Deserialize() => _dataset.Deserialize(_adapter, _payload);
}
