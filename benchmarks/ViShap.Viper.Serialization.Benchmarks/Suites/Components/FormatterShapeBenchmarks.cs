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
/// and SCALE-06.
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

    private MemoryStream _destination = null!;
    private ValueWriter _writer = null!;
    private GraphWriter _graphWriter = null!;

    private MemoryStream _scalarSource = null!;
    private ValueReader _scalarReader = null!;

    private MemoryStream _sequenceSource = null!;
    private GraphReader _sequenceReader = null!;

    private MemoryStream _mapSource = null!;
    private GraphReader _mapReader = null!;

    private MemoryStream _compositeSource = null!;
    private GraphReader _compositeReader = null!;

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

        _destination = new MemoryStream(Elements * 32);
        _writer = new ValueWriter(_destination, _operation);
        _graphWriter = new GraphWriter(_writer, _operation);

        (_scalarSource, _scalarReader) = ComponentFixtures.Decoder(
            _operation,
            ComponentFixtures.Encode(_operation, writer =>
            {
                for (var i = 0; i < ScalarOperations; i++)
                {
                    _guidFormatter.Write(writer, Scalar, typeof(Guid));
                }
            }));

        (_sequenceSource, _sequenceReader) = Graph(graph => graph.WriteValue(_sequence, typeof(List<int>)));
        (_mapSource, _mapReader) = Graph(graph => graph.WriteValue(_map, typeof(Dictionary<int, long>)));
        (_compositeSource, _compositeReader) = Graph(graph => graph.WriteValue(_composite, _compositeType));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _destination.Dispose();
        _scalarSource.Dispose();
        _sequenceSource.Dispose();
        _mapSource.Dispose();
        _compositeSource.Dispose();
    }

    [Benchmark(Description = "MICRO-06 scalar write", OperationsPerInvoke = ScalarOperations)]
    public long ScalarWrite()
    {
        _destination.Position = 0;

        for (var i = 0; i < ScalarOperations; i++)
        {
            _guidFormatter.Write(_writer, Scalar, typeof(Guid));
        }

        return _destination.Position;
    }

    [Benchmark(Description = "MICRO-06 scalar read", OperationsPerInvoke = ScalarOperations)]
    public object ScalarRead()
    {
        _scalarSource.Position = 0;
        object value = Scalar;

        for (var i = 0; i < ScalarOperations; i++)
        {
            value = _guidFormatter.Read(_scalarReader, typeof(Guid));
        }

        return value;
    }

    [Benchmark(Description = "MICRO-06 sequence write")]
    public long SequenceWrite()
    {
        _destination.Position = 0;
        _graphWriter.WriteValue(_sequence, typeof(List<int>));
        return _destination.Position;
    }

    [Benchmark(Description = "MICRO-06 sequence read")]
    public object? SequenceRead()
    {
        _sequenceSource.Position = 0;
        return _sequenceReader.ReadValue(typeof(List<int>));
    }

    [Benchmark(Description = "MICRO-06 map write")]
    public long MapWrite()
    {
        _destination.Position = 0;
        _graphWriter.WriteValue(_map, typeof(Dictionary<int, long>));
        return _destination.Position;
    }

    [Benchmark(Description = "MICRO-06 map read")]
    public object? MapRead()
    {
        _mapSource.Position = 0;
        return _mapReader.ReadValue(typeof(Dictionary<int, long>));
    }

    [Benchmark(Description = "MICRO-06 composite write")]
    public long CompositeWrite()
    {
        _destination.Position = 0;
        _graphWriter.WriteValue(_composite, _compositeType);
        return _destination.Position;
    }

    [Benchmark(Description = "MICRO-06 composite read")]
    public object? CompositeRead()
    {
        _compositeSource.Position = 0;
        return _compositeReader.ReadValue(_compositeType);
    }

    private (MemoryStream Source, GraphReader Reader) Graph(Action<GraphWriter> write)
    {
        using var buffer = new MemoryStream(Elements * 32);
        write(new GraphWriter(new ValueWriter(buffer, _operation), _operation));

        var source = new MemoryStream(buffer.ToArray(), writable: false);
        return (source, new GraphReader(new ValueReader(source, _operation), _operation));
    }
}
