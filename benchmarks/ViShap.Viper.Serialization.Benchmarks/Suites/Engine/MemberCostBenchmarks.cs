using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;
using Build = ViShap.Viper.Serialization.Benchmarks.DataSets.Build;
using DeterministicRandom = ViShap.Viper.Serialization.Benchmarks.DataSets.DeterministicRandom;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Engine;

/// <summary>
/// The cost of writing and reading the members of an object, under generated contracts and under the
/// contracts the reflection path builds: a small object, an ordinary business object, and a hundred
/// members in a positional and in a keyed layout.
/// </summary>
[MemoryDiagnoser]
public class MemberCostBenchmarks
{
    private Func<byte[]> _write = null!;
    private Func<object?> _read = null!;

    [Params("Generated", "Reflected")]
    public string Contracts { get; set; } = "Generated";

    [Params("DATA-01", "DATA-02", "SCALE-04/p100", "SCALE-04/k100")]
    public string Data { get; set; } = "DATA-01";

    [GlobalSetup]
    public void Setup()
    {
        var options = Contracts == "Generated"
            ? BinarySerializerOptions.Configure().WithContracts(BenchmarkContracts.Default).RequireGeneratedContracts().Build()
            : BinarySerializerOptions.Default;
        var serializer = new BinarySerializer(options);

        switch (Data)
        {
            case "DATA-01": Bind(serializer, Build.TinyFlat(new DeterministicRandom(0x0000_0001))); break;
            case "DATA-02": Bind(serializer, Build.MediumObject(new DeterministicRandom(0x0000_0002))); break;
            case "SCALE-04/p100": Bind(serializer, WideModels.Populate(new WidePositional100(), new DeterministicRandom(0x0000_0400))); break;
            default: Bind(serializer, WideModels.Populate(new WideKeyed100(), new DeterministicRandom(0x0000_0401))); break;
        }
    }

    private void Bind<T>(BinarySerializer serializer, T value)
    {
        byte[] frame = serializer.Serialize(value);
        _write = () => serializer.Serialize(value);
        _read = () => serializer.Deserialize<T>(frame);
    }

    [Benchmark(Description = "write")]
    public byte[] Write() => _write();

    [Benchmark(Description = "read")]
    public object? Read() => _read();
}
