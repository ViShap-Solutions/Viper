using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-06 — one formatter per shape family: a scalar, a sequence, a map and a composite.
/// </summary>
/// <remarks>
/// A scalar is driven through its own <see cref="IScalarFormatter"/>, which is the whole of its
/// encoding. The other three are driven through <see cref="GraphWriter"/> and
/// <see cref="GraphReader"/>, because the engine owns the count, the loop, the depth scope and the
/// node budget by design: there is no formatter-only path through a container, and writing one here
/// would measure the harness instead of the library. Those three cells are therefore a shape family
/// plus the engine work that surrounds it, and MICRO-02, MICRO-03 and MICRO-07 are what that work is
/// composed of.
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

    private SerializationOperation _operation = null!;
    private IScalarFormatter _guidFormatter = null!;

    private PayloadBuffer _destination = null!;
    private GraphWriter _graphWriter = null!;
    private GraphReader _graphReader = null!;

    private byte[] _scalarPayload = [];
    private byte[] _sequencePayload = [];
    private byte[] _mapPayload = [];
    private byte[] _compositePayload = [];

    private List<int> _sequence = [];
    private Dictionary<int, long> _map = [];

    /// <summary>Boxed once, so the timed region holds the write and not a boxing allocation.</summary>
    private object _composite = null!;

    private Type _compositeType = null!;

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.UnboundedTotals();
        _guidFormatter = (IScalarFormatter)FormatterRegistry.Resolve(typeof(Guid))!;

        _sequence = [.. Enumerable.Range(0, Elements)];
        _map = Enumerable.Range(0, Elements).ToDictionary(index => index, index => (long)index * 7);
        _composite = (42, "composite", 0.5);
        _compositeType = _composite.GetType();

        _destination = new PayloadBuffer(_operation.Limits.MaxPayloadBytes, "payload");
        _graphWriter = new GraphWriter(_operation);
        _graphReader = new GraphReader(_operation);

        _scalarPayload = ComponentFixtures.Encode(_operation, (ref WireWriter writer) =>
        {
            for (var i = 0; i < ScalarOperations; i++)
            {
                _guidFormatter.Write(ref writer, Scalar, typeof(Guid));
            }
        });

        _sequencePayload = ComponentFixtures.Encode(_operation, (ref WireWriter writer) =>
            _graphWriter.WriteValue(ref writer, _sequence, typeof(List<int>)));
        _mapPayload = ComponentFixtures.Encode(_operation, (ref WireWriter writer) =>
            _graphWriter.WriteValue(ref writer, _map, typeof(Dictionary<int, long>)));
        _compositePayload = ComponentFixtures.Encode(_operation, (ref WireWriter writer) =>
            _graphWriter.WriteValue(ref writer, _composite, _compositeType));
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "MICRO-06 scalar write", OperationsPerInvoke = ScalarOperations)]
    public long ScalarWrite()
    {
        var writer = new WireWriter(_destination, _operation);

        for (var i = 0; i < ScalarOperations; i++)
        {
            _guidFormatter.Write(ref writer, Scalar, typeof(Guid));
        }

        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 scalar read", OperationsPerInvoke = ScalarOperations)]
    public object ScalarRead()
    {
        var reader = new WireReader(_scalarPayload, _operation);
        object value = Scalar;

        for (var i = 0; i < ScalarOperations; i++)
        {
            value = _guidFormatter.Read(ref reader, typeof(Guid));
        }

        return value;
    }

    [Benchmark(Description = "MICRO-06 sequence write")]
    public long SequenceWrite()
    {
        var writer = new WireWriter(_destination, _operation);
        _graphWriter.WriteValue(ref writer, _sequence, typeof(List<int>));
        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 sequence read")]
    public object? SequenceRead()
    {
        var reader = new WireReader(_sequencePayload, _operation);
        return _graphReader.ReadValue(ref reader, typeof(List<int>));
    }

    [Benchmark(Description = "MICRO-06 map write")]
    public long MapWrite()
    {
        var writer = new WireWriter(_destination, _operation);
        _graphWriter.WriteValue(ref writer, _map, typeof(Dictionary<int, long>));
        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 map read")]
    public object? MapRead()
    {
        var reader = new WireReader(_mapPayload, _operation);
        return _graphReader.ReadValue(ref reader, typeof(Dictionary<int, long>));
    }

    [Benchmark(Description = "MICRO-06 composite write")]
    public long CompositeWrite()
    {
        var writer = new WireWriter(_destination, _operation);
        _graphWriter.WriteValue(ref writer, _composite, _compositeType);
        return Complete(ref writer);
    }

    [Benchmark(Description = "MICRO-06 composite read")]
    public object? CompositeRead()
    {
        var reader = new WireReader(_compositePayload, _operation);
        return _graphReader.ReadValue(ref reader, _compositeType);
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
