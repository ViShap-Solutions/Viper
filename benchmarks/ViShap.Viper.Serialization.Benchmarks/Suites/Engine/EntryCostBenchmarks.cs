using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Pipeline;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;
using Build = ViShap.Viper.Serialization.Benchmarks.DataSets.Build;
using DeterministicRandom = ViShap.Viper.Serialization.Benchmarks.DataSets.DeterministicRandom;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Engine;

/// <summary>
/// The layers of one call over DATA-01, peeled one at a time: the public entry, the format pipeline
/// reached through its interface and directly, and the engine alone over the payload. The differences
/// between neighbouring cells are what each layer costs.
/// </summary>
[MemoryDiagnoser]
public class EntryCostBenchmarks
{
    private BinarySerializer _serializer = null!;
    private BinarySerializerOptions _options = null!;
    private IFormatPipeline _pipeline = null!;
    private V1FormatPipeline _direct = null!;
    private TinyFlat _value = null!;
    private byte[] _frame = [];

    [GlobalSetup]
    public void Setup()
    {
        _options = BinarySerializerOptions.Configure()
            .WithContracts(BenchmarkContracts.Default)
            .RequireGeneratedContracts()
            .Build();
        _serializer = new BinarySerializer(_options);
        _direct = new V1FormatPipeline(_options.Compression, _options.Checksum, _options.Encryption, _options.KeyId, _options.Catalog);
        _pipeline = _direct;
        _value = Build.TinyFlat(new DeterministicRandom(0x0000_0001));
        _frame = _serializer.Serialize(_value, BenchmarkContracts.Default.TinyFlat);
    }

    private int HeaderLength() =>
        Metadata.BinaryFormatHeaderV1.TryReadPlain(_frame, out _, out int length) ? length : throw new InvalidOperationException();

    private OperationState State() =>
        new(_options.Limits, _options.Keys, _options.PreserveReferences, _options.RequireEncryption,
            _options.RequireChecksum, _options.ContractSet);

    [Benchmark(Description = "W1 Serialize → byte[]")]
    public byte[] PublicWrite() => _serializer.Serialize(_value, BenchmarkContracts.Default.TinyFlat);

    [Benchmark(Description = "W2 pipeline through the interface")]
    public byte[] InterfaceWrite()
    {
        var state = State();
        using var frame = _pipeline.Write(_value, ref state);
        return frame.ToArray();
    }

    [Benchmark(Description = "W3 pipeline called directly")]
    public byte[] DirectWrite()
    {
        var state = State();
        using var frame = _direct.Write(_value, ref state);
        return frame.ToArray();
    }

    [Benchmark(Description = "R1 Deserialize ← byte[]")]
    public object? PublicRead() => _serializer.Deserialize(_frame, BenchmarkContracts.Default.TinyFlat);

    [Benchmark(Description = "R2 pipeline through the interface")]
    public object? InterfaceRead()
    {
        var state = State();
        return _pipeline.Read<TinyFlat>(_frame, default, ref state, out _);
    }

    [Benchmark(Description = "R3 pipeline called directly")]
    public object? DirectRead()
    {
        var state = State();
        return _direct.Read<TinyFlat>(_frame, default, ref state, out _);
    }

    [Benchmark(Description = "R4 the engine alone over the payload")]
    public object? EngineRead()
    {
        var state = State();
        var reader = new WireReader(_frame.AsSpan(HeaderLength()), ref state);
        return Graph.ReadRoot<TinyFlat>(ref reader, target: null, preserveReferences: false);
    }
}
