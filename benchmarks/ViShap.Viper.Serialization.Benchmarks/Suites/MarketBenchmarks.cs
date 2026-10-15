using System.Buffers;
using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Adapters.Market;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// B2 — the comparative core through the buffered entry points (WL-01, WL-04, WL-06): every library
/// configuration of §5.2 over every dataset of its tiers that verified, each in its own models. A case
/// carries its configuration and dataset; the tier tables are drawn from <c>tiers.csv</c>, never from
/// the case list.
/// </summary>
[MemoryDiagnoser]
public class MarketBufferedBenchmarks
{
    private MarketAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<MarketCase> Cases => MarketCases.All.Where(@case => @case.DatasetId != MarketVerifier.Evolution);

    [ParamsSource(nameof(Cases))]
    public MarketCase Case { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_adapter, _dataset) = MarketSetup.Verified(Case);
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
/// B2 — the streaming family (WL-02, WL-05) over the core datasets: every adapter writes into a stream
/// pre-sized for its own payload and reset, never reallocated, and reads from a reset stream (FAIR-07).
/// </summary>
[MemoryDiagnoser]
public class MarketStreamBenchmarks
{
    private MarketAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private MemoryStream _destination = null!;
    private MemoryStream _source = null!;

    public static IEnumerable<MarketCase> Cases => MarketSetup.Core(MarketCases.All);

    [ParamsSource(nameof(Cases))]
    public MarketCase Case { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_adapter, _dataset) = MarketSetup.Verified(Case);

        using var probe = new MemoryStream();
        _dataset.SerializeTo(_adapter, probe);

        _destination = new MemoryStream((int)probe.Length);
        _source = new MemoryStream(probe.ToArray(), writable: false);
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
/// B2 — the buffer family (WL-21, WL-22) over the core datasets, for every library with buffer entry
/// points: a write into one reset <see cref="ArrayBufferWriter{T}"/> and a read from a sequence of four
/// segments (FAIR-07). A library without the entry points has no case here, and its cell is
/// <c>Unsupported</c>.
/// </summary>
[MemoryDiagnoser]
public class MarketBufferBenchmarks
{
    private IBufferWriterSerializer _adapter = null!;
    private Dataset _dataset = null!;
    private ArrayBufferWriter<byte> _destination = null!;
    private byte[] _payload = [];
    private ReadOnlySequence<byte> _segmented;

    public static IEnumerable<MarketCase> Cases =>
        MarketSetup.Core(MarketCases.All).Where(@case => @case.Adapter is IBufferWriterSerializer);

    [ParamsSource(nameof(Cases))]
    public MarketCase Case { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (adapter, dataset) = MarketSetup.Verified(Case);
        _adapter = (IBufferWriterSerializer)adapter;
        _dataset = dataset;

        var writer = new ArrayBufferWriter<byte>();
        _dataset.SerializeInto(_adapter, writer);
        _payload = writer.WrittenSpan.ToArray();

        _destination = new ArrayBufferWriter<byte>(_payload.Length);
        _segmented = Segments.Of(_payload, count: 4);
    }

    [Benchmark(Description = "WL-21 serialize → IBufferWriter")]
    public int SerializeToBufferWriter()
    {
        _destination.ResetWrittenCount();
        _dataset.SerializeInto(_adapter, _destination);
        return _destination.WrittenCount;
    }

    [Benchmark(Description = "WL-22 deserialize ← ReadOnlySequence (4 segments)")]
    public object? DeserializeFromSequence() => _dataset.DeserializeSequence(_adapter, _segmented);
}

/// <summary>
/// B2 — WL-22's span read over the core datasets, for every library that reads a
/// <see cref="ReadOnlySpan{T}"/>. A library that reads only a memory or a sequence has no case here.
/// </summary>
[MemoryDiagnoser]
public class MarketSpanBenchmarks
{
    private ISpanDeserializer _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<MarketCase> Cases =>
        MarketSetup.Core(MarketCases.All).Where(@case => @case.Adapter is ISpanDeserializer);

    [ParamsSource(nameof(Cases))]
    public MarketCase Case { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (adapter, dataset) = MarketSetup.Verified(Case);
        _adapter = (ISpanDeserializer)adapter;
        _dataset = dataset;
        _payload = _dataset.Serialize(adapter);
    }

    [Benchmark(Description = "WL-22 deserialize ← ReadOnlySpan")]
    public object? DeserializeFromSpan() => _dataset.DeserializeSpan(_adapter, _payload);
}

/// <summary>
/// B2 — WL-15, the skipping side of evolution: the orders of DATA-13 written by the first model and read
/// by the model that removed, added and reordered keys, for every library whose evolution pair verified.
/// </summary>
[MemoryDiagnoser]
public class MarketEvolutionBenchmarks
{
    private Func<byte[], object?> _read = null!;
    private byte[] _payload = [];

    public static IEnumerable<MarketCase> Cases => MarketCases.All.Where(@case => @case.DatasetId == MarketVerifier.Evolution);

    [ParamsSource(nameof(Cases))]
    public MarketCase Case { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var adapter = Case.Adapter;
        _payload = MarketCorpus.Find(adapter.Family, "DATA-13").Serialize(adapter);

        var readAs = ModelFamilies.Map(typeof(List<Models.Viper.KeyedOrderV2>), adapter.Family);
        var reader = (IEvolutionRead)Activator.CreateInstance(typeof(EvolutionRead<>).MakeGenericType(readAs), adapter)!;
        _read = reader.Read;

        if (_read(_payload) is null)
            throw new InvalidOperationException($"{Case}: the evolution read restored nothing.");
    }

    [Benchmark(Description = "WL-15 deserialize evolved ← byte[]")]
    public object? DeserializeEvolved() => _read(_payload);

    private interface IEvolutionRead
    {
        object? Read(byte[] payload);
    }

    private sealed class EvolutionRead<T>(IBufferedSerializer serializer) : IEvolutionRead
    {
        public object? Read(byte[] payload) => serializer.Deserialize<T>(payload);
    }
}

/// <summary>The setup every comparative suite shares.</summary>
internal static class MarketSetup
{
    /// <summary>The datasets the entry-point families measure, as <see cref="Corpus.Core"/> names them.</summary>
    internal static IEnumerable<MarketCase> Core(IEnumerable<MarketCase> cases)
    {
        var core = Corpus.Core.Select(dataset => dataset.Id).ToHashSet(StringComparer.Ordinal);
        return cases.Where(@case => core.Contains(@case.DatasetId));
    }

    /// <summary>
    /// The case's adapter and dataset, after one more round trip: a recorded verification never stands
    /// in for the pair the suite is about to time (FAIR-24).
    /// </summary>
    internal static (MarketAdapter Adapter, Dataset Dataset) Verified(MarketCase @case)
    {
        var adapter = @case.Adapter;
        var dataset = @case.Dataset;

        var restored = dataset.Deserialize(adapter, dataset.Serialize(adapter));
        if (!StructuralComparer.Equal(dataset.BoxedValue, restored, out var difference))
            throw new InvalidOperationException($"{@case} no longer verifies: {difference}");

        return (adapter, dataset);
    }
}
