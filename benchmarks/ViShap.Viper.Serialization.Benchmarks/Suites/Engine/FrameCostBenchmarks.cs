using System.Buffers;
using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;
using Build = ViShap.Viper.Serialization.Benchmarks.DataSets.Build;
using DeterministicRandom = ViShap.Viper.Serialization.Benchmarks.DataSets.DeterministicRandom;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Engine;

/// <summary>
/// The cost of the frame and of the call around the engine, through every destination and source a
/// frame passes: one small object (DATA-01) and one ordinary business object (DATA-02), written and read
/// with generated contracts, as a versioned frame with no service record and as a headerless payload.
/// </summary>
[MemoryDiagnoser]
public class FrameCostBenchmarks
{
    private BinarySerializer _serializer = null!;
    private object _value = null!;
    private byte[] _frame = [];
    private ArrayBufferWriter<byte> _bufferWriter = null!;
    private MemoryStream _destination = null!;
    private MemoryStream _source = null!;
    private Func<byte[]> _toArray = null!;
    private Action _toBufferWriter = null!;
    private Action _toStream = null!;
    private Func<object?> _fromArray = null!;
    private Func<object?> _fromStream = null!;

    [Params("DATA-01", "DATA-02")]
    public string Data { get; set; } = "DATA-01";

    [Params(1, 0)]
    public int Version { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var builder = BinarySerializerOptions.Configure()
            .WithContracts(BenchmarkContracts.Default)
            .RequireGeneratedContracts();
        if (Version == 0)
            builder = builder.WithVersion(0).AllowV0Fallback();

        _serializer = new BinarySerializer(builder.Build());
        _bufferWriter = new ArrayBufferWriter<byte>(4096);
        _destination = new MemoryStream(4096);

        if (Data == "DATA-01")
            Bind(Build.TinyFlat(new DeterministicRandom(0x0000_0001)), BenchmarkContracts.Default.TinyFlat);
        else
            Bind(Build.MediumObject(new DeterministicRandom(0x0000_0002)), BenchmarkContracts.Default.MediumObject);

        _frame = _toArray();
        _source = new MemoryStream(_frame, writable: false);
    }

    private void Bind<T>(T value, BinaryTypeInfo<T> typeInfo)
    {
        _value = value!;
        _toArray = () => _serializer.Serialize(value, typeInfo);
        _toBufferWriter = () => _serializer.Serialize(_bufferWriter, value, typeInfo);
        _toStream = () => _serializer.Serialize(_destination, value, typeInfo);
        _fromArray = () => _serializer.Deserialize(_frame, typeInfo);
        _fromStream = () => _serializer.Deserialize(_source, typeInfo);
    }

    [Benchmark(Description = "write → byte[]")]
    public byte[] WriteArray() => _toArray();

    [Benchmark(Description = "write → IBufferWriter")]
    public int WriteBufferWriter()
    {
        _bufferWriter.ResetWrittenCount();
        _toBufferWriter();
        return _bufferWriter.WrittenCount;
    }

    [Benchmark(Description = "write → Stream")]
    public long WriteStream()
    {
        _destination.Position = 0;
        _toStream();
        return _destination.Position;
    }

    [Benchmark(Description = "read ← byte[]")]
    public object? ReadArray() => _fromArray();

    [Benchmark(Description = "read ← Stream")]
    public object? ReadStream()
    {
        _source.Position = 0;
        return _fromStream();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _destination.Dispose();
        _source.Dispose();
    }
}
