using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-06 — one codec per shape family: a scalar, a sequence, a map and a composite, each found
/// through <see cref="FormatterCache{T}"/>.
/// </summary>
/// <remarks>
/// A scalar is driven through its codec, which for a value type is the formatter's encoding and
/// nothing else. The other three are driven through the engine's entry for a payload,
/// <see cref="Graph"/>, because the codec owns the count, the loop, the depth scope and the node
/// budget by design: there is no shape-only path through a container, and writing one here would
/// measure the harness instead of the library. Those three cells are therefore a shape family plus
/// the engine work that surrounds it — the traversal opened and closed once per payload — and
/// MICRO-02, MICRO-03 and MICRO-07 are what that work is composed of.
/// <para>
/// A container cell is one container of a hundred elements, so the per-element figure is the mean
/// divided by a hundred. Explains the shape rows of WL-01 and WL-04, and the intercept of SCALE-01
/// and SCALE-06. A write invocation fills the payload buffer from empty and returns its segments to
/// the pool, as one serialization does.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class FormatterShapeBenchmarks
{
    private const int Elements = 100;
    private const int ScalarOperations = 1_000;

    private static readonly Guid Scalar = new("8B7C0C5A-3E4D-4F2B-9A1E-6D5C4B3A2918");

    private OperationState _operation;
    private Codec<Guid> _guid = null!;

    private PayloadBuffer _destination = null!;

    private byte[] _scalarPayload = [];
    private byte[] _sequencePayload = [];
    private byte[] _mapPayload = [];
    private byte[] _compositePayload = [];

    private List<int> _sequence = [];
    private Dictionary<int, long> _map = [];
    private (int, string, double) _composite;

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.UnboundedTotals();
        _guid = FormatterCache<Guid>.Instance;

        _sequence = [.. Enumerable.Range(0, Elements)];
        _map = Enumerable.Range(0, Elements).ToDictionary(index => index, index => (long)index * 7);
        _composite = (42, "composite", 0.5);

        _destination = new PayloadBuffer(_operation.Limits.MaxPayloadBytes, "payload");

        _scalarPayload = ComponentFixtures.Encode(ref _operation, (ref WireWriter writer) =>
        {
            for (var i = 0; i < ScalarOperations; i++)
            {
                _guid.Write(ref writer, Scalar);
            }
        });

        _sequencePayload = ComponentFixtures.Encode(ref _operation, (ref WireWriter writer) =>
            Graph.WriteRoot(ref writer, _sequence, preserveReferences: false));
        _mapPayload = ComponentFixtures.Encode(ref _operation, (ref WireWriter writer) =>
            Graph.WriteRoot(ref writer, _map, preserveReferences: false));
        _compositePayload = ComponentFixtures.Encode(ref _operation, (ref WireWriter writer) =>
            Graph.WriteRoot(ref writer, _composite, preserveReferences: false));
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "MICRO-06 scalar write", OperationsPerInvoke = ScalarOperations)]
    public long ScalarWrite()
    {
        var writer = new WireWriter(_destination, ref _operation);

        for (var i = 0; i < ScalarOperations; i++)
        {
            _guid.Write(ref writer, Scalar);
        }

        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 scalar read", OperationsPerInvoke = ScalarOperations)]
    public Guid ScalarRead()
    {
        var reader = new WireReader(_scalarPayload, ref _operation);
        var value = Scalar;

        for (var i = 0; i < ScalarOperations; i++)
        {
            value = _guid.Read(ref reader);
        }

        return value;
    }

    [Benchmark(Description = "MICRO-06 sequence write")]
    public long SequenceWrite()
    {
        var writer = new WireWriter(_destination, ref _operation);
        Graph.WriteRoot(ref writer, _sequence, preserveReferences: false);
        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 sequence read")]
    public List<int>? SequenceRead()
    {
        var reader = new WireReader(_sequencePayload, ref _operation);
        return Graph.ReadRoot<List<int>>(ref reader, target: null, preserveReferences: false);
    }

    [Benchmark(Description = "MICRO-06 map write")]
    public long MapWrite()
    {
        var writer = new WireWriter(_destination, ref _operation);
        Graph.WriteRoot(ref writer, _map, preserveReferences: false);
        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 map read")]
    public Dictionary<int, long>? MapRead()
    {
        var reader = new WireReader(_mapPayload, ref _operation);
        return Graph.ReadRoot<Dictionary<int, long>>(ref reader, target: null, preserveReferences: false);
    }

    [Benchmark(Description = "MICRO-06 composite write")]
    public long CompositeWrite()
    {
        var writer = new WireWriter(_destination, ref _operation);
        Graph.WriteRoot(ref writer, _composite, preserveReferences: false);
        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 composite read")]
    public (int, string, double) CompositeRead()
    {
        var reader = new WireReader(_compositePayload, ref _operation);
        return Graph.ReadRoot<(int, string, double)>(ref reader, target: default, preserveReferences: false);
    }

    /// <summary>Commits what the writer holds and empties the buffer for the next invocation.</summary>
    private long Complete(ref WireWriter writer)
    {
        writer.Flush();
        long written = _destination.Length;
        _destination.Dispose();
        return written;
    }
}
