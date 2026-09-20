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

/// <summary>§19 SCALE-02 — payload size, from a kilobyte to the neighbourhood of `MaxPayloadBytes`.</summary>
/// <remarks>
/// The payload is a batch of records rather than one byte blob. Bulk array data cannot reach these sizes
/// under the default policy at all: a `byte[]` spends the element budget per byte, so about ten megabytes
/// of arrays exhausts `MaxTotalElements` however the bytes are split, and no number of smaller arrays
/// gets around it (PERF-01). A record spends one element and one graph node regardless of how many bytes
/// it encodes to, which is what lets the curve reach 64 MB inside every default ceiling and stay
/// comparable with the profiles measured elsewhere.
/// <para>
/// The record count for a target size is derived by measuring a probe batch, and the setup refuses a
/// batch that lands more than five percent from its target, so a published cell never claims a size it
/// did not reach. The derivation depends on the encoding alone, so it is the same on any machine.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class PayloadSizeScalingBenchmarks
{
    private const int Probe = 1_000;

    private ViperAdapter _adapter = null!;
    private List<TinyFlat> _value = [];
    private byte[] _payload = [];

    [Params(1_000, 64_000, 1_000_000, 16_000_000, 64_000_000)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);

        var rng = new DeterministicRandom(0x0000_5002);
        var probe = new List<TinyFlat>(Probe);

        for (var i = 0; i < Probe; i++)
        {
            probe.Add(Build.TinyFlat(rng));
        }

        double perRecord = (double)_adapter.Serialize(probe).Length / Probe;
        int count = Math.Max((int)(Bytes / perRecord), 1);

        _value = new List<TinyFlat>(count);
        var records = new DeterministicRandom(0x0000_5002);

        for (var i = 0; i < count; i++)
        {
            _value.Add(Build.TinyFlat(records));
        }

        _payload = _adapter.Serialize(_value);

        // The axis of the published curve is the target, so the batch has to land on it. A derivation
        // that drifted would put a cell under a size it never reached, which is worse than a coarser axis.
        double drift = Math.Abs((double)_payload.Length - Bytes) / Bytes;

        if (drift > 0.05)
        {
            throw new InvalidOperationException(
                $"A batch aimed at {Bytes:N0} bytes encoded to {_payload.Length:N0}, off by " +
                $"{drift:P1}. The curve would claim a size it did not reach.");
        }
    }

    [Benchmark(Description = "SCALE-02 serialize")]
    public byte[] Serialize() => _adapter.Serialize(_value);

    [Benchmark(Description = "SCALE-02 deserialize")]
    public object? Deserialize() => _adapter.Deserialize<List<TinyFlat>>(_payload);
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

/// <summary>
/// §19 SCALE-04 — member count, positional against keyed, at five sizes.
/// </summary>
/// <remarks>
/// The two layouts are measured over the same member cycle in the same order, so a difference between
/// them is the layout and nothing else: a keyed field carries its key and a patched length, a positional
/// one carries neither. The 200-member positional type is DATA-18 itself, which makes the 200 row the
/// steady-state half of DIFF-03; the first-use half is the contract construction the component runner
/// reports for the same types.
/// <para>
/// The value is selected once and reached through a delegate, which costs one call per invocation in
/// every cell of the table alike.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class MemberCountScalingBenchmarks
{
    private Func<byte[]> _serialize = null!;
    private Func<byte[], object?> _deserialize = null!;
    private byte[] _payload = [];

    [Params(5, 20, 50, 100, 200)]
    public int Members { get; set; }

    [Params(false, true)]
    public bool Keyed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var adapter = new ViperAdapter(ViperProfile.Default);
        var rng = new DeterministicRandom(0x0000_5004UL + (ulong)Members);

        if (Keyed)
        {
            switch (Members)
            {
                case 5: Bind(WideModels.Populate(new WideKeyed005(), rng)); break;
                case 20: Bind(WideModels.Populate(new WideKeyed020(), rng)); break;
                case 50: Bind(WideModels.Populate(new WideKeyed050(), rng)); break;
                case 100: Bind(WideModels.Populate(new WideKeyed100(), rng)); break;
                default: Bind(WideModels.Populate(new WideKeyed200(), rng)); break;
            }
        }
        else
        {
            switch (Members)
            {
                case 5: Bind(WideModels.Populate(new WidePositional005(), rng)); break;
                case 20: Bind(WideModels.Populate(new WidePositional020(), rng)); break;
                case 50: Bind(WideModels.Populate(new WidePositional050(), rng)); break;
                case 100: Bind(WideModels.Populate(new WidePositional100(), rng)); break;
                default: Bind(WideModels.Populate(new WideObject(), rng)); break;
            }
        }

        _payload = _serialize();

        void Bind<T>(T value)
        {
            _serialize = () => adapter.Serialize(value);
            _deserialize = payload => adapter.Deserialize<T>(payload);
        }
    }

    [Benchmark(Description = "SCALE-04 serialize")]
    public byte[] Serialize() => _serialize();

    [Benchmark(Description = "SCALE-04 deserialize")]
    public object? Deserialize() => _deserialize(_payload);
}
