using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// §19 SCALE-01 — element count. A single size is a point; a curve is a property, so the same shape
/// is measured at six counts and the slope is what the result says.
/// </summary>
[MemoryDiagnoser]
public class ElementCountScalingBenchmarks
{
    private ViperAdapter _adapter = null!;
    private List<TinyFlat> _value = [];
    private byte[] _payload = [];

    [Params(1, 10, 100, 1_000, 10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);

        var rng = new DeterministicRandom(0x0000_5001);
        _value = new List<TinyFlat>(Count);

        for (var i = 0; i < Count; i++)
        {
            _value.Add(Build.TinyFlat(rng));
        }

        _payload = _adapter.Serialize(_value);
    }

    [Benchmark(Description = "SCALE-01 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-01 deserialize")]
    public object? Deserialize() => _adapter.Deserialize<List<TinyFlat>>(_payload);
}

/// <summary>§19 SCALE-02 — payload size, measured on a shape whose traversal is constant.</summary>
[MemoryDiagnoser]
public class PayloadSizeScalingBenchmarks
{
    private ViperAdapter _adapter = null!;
    private BlobEnvelope _value = new();
    private byte[] _payload = [];

    [Params(1_000, 64_000, 250_000, 900_000)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);
        _value = ByteBlobDataset.Blob(0x0000_5002, Bytes);
        _payload = _adapter.Serialize(_value);
    }

    [Benchmark(Description = "SCALE-02 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-02 deserialize")]
    public object? Deserialize() => _adapter.Deserialize<BlobEnvelope>(_payload);
}

/// <summary>§19 SCALE-03 — depth, up to just below the default ceiling of 512 (§5.1).</summary>
[MemoryDiagnoser]
public class DepthScalingBenchmarks
{
    private ViperAdapter _adapter = null!;
    private DeepNode _value = new();
    private byte[] _payload = [];

    [Params(1, 5, 25, 100, 500)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);

        var rng = new DeterministicRandom(0x0000_5003UL + (ulong)Depth);
        DeepNode? child = null;

        for (var level = Depth; level >= 1; level--)
        {
            child = new DeepNode
            {
                Level = level,
                Label = rng.NextAscii(4, 10),
                Payload = rng.NextInt64(),
                Child = child,
            };
        }

        _value = child!;
        _payload = _adapter.Serialize(_value);
    }

    [Benchmark(Description = "SCALE-03 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-03 deserialize")]
    public object? Deserialize() => _adapter.Deserialize<DeepNode>(_payload);
}

/// <summary>§19 SCALE-05 — string length, ASCII against multi-byte, at equal character counts.</summary>
[MemoryDiagnoser]
public class StringScalingBenchmarks
{
    private ViperAdapter _adapter = null!;
    private string _value = string.Empty;
    private byte[] _payload = [];

    [Params(8, 256, 4_000, 64_000)]
    public int Length { get; set; }

    [Params(false, true)]
    public bool MultiByte { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);

        _value = MultiByte
            ? string.Concat(Enumerable.Repeat("日本", Length / 2))
            : new string('a', Length);

        _payload = _adapter.Serialize(_value);
    }

    [Benchmark(Description = "SCALE-05 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-05 deserialize")]
    public string? Deserialize() => _adapter.Deserialize<string>(_payload);
}

/// <summary>§19 SCALE-06 — dictionary size.</summary>
[MemoryDiagnoser]
public class DictionaryScalingBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dictionary<string, ScalarRecord> _value = [];
    private byte[] _payload = [];

    [Params(10, 100, 1_000, 10_000, 100_000)]
    public int Entries { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);

        var rng = new DeterministicRandom(0x0000_5006);
        _value = new Dictionary<string, ScalarRecord>(Entries);

        for (var i = 0; i < Entries; i++)
        {
            _value[$"entry-{i:D6}"] = Build.ScalarRecord(rng);
        }

        _payload = _adapter.Serialize(_value);
    }

    [Benchmark(Description = "SCALE-06 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-06 deserialize")]
    public object? Deserialize() =>
        _adapter.Deserialize<Dictionary<string, ScalarRecord>>(_payload);
}

/// <summary>
/// §19 SCALE-07 — sharing density. The same node count with a growing share of instances reachable
/// by more than one path, measured with reference framing on and off, so what the option costs and
/// what it saves are one curve.
/// </summary>
[MemoryDiagnoser]
public class SharingDensityBenchmarks
{
    private const int Nodes = 2_000;

    private ViperAdapter _adapter = null!;
    private List<DagNode> _value = [];
    private byte[] _payload = [];

    [Params(0, 10, 50, 90)]
    public int SharedPercent { get; set; }

    [Params(ViperProfile.Default, ViperProfile.PreserveReferences)]
    public ViperProfile Profile { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(Profile);

        var rng = new DeterministicRandom(0x0000_5007UL + (ulong)SharedPercent);
        var shared = new DagNode { Id = -1, Name = "shared", Weight = 1 };

        _value = new List<DagNode>(Nodes);

        for (var i = 0; i < Nodes; i++)
        {
            if (rng.Next(100) < SharedPercent)
            {
                _value.Add(shared);
                continue;
            }

            _value.Add(new DagNode
            {
                Id = i,
                Name = rng.NextAscii(8, 16),
                Weight = rng.NextDouble(),
            });
        }

        _payload = _adapter.Serialize(_value);
    }

    [Benchmark(Description = "SCALE-07 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-07 deserialize")]
    public object? Deserialize() => _adapter.Deserialize<List<DagNode>>(_payload);
}
