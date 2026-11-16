using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Engine;

/// <summary>
/// The cost of the engine's element loops and of text: lists of objects, a dictionary, short and long
/// strings in ASCII and beyond it, numeric arrays and one instance of every container family, under
/// generated contracts.
/// </summary>
[MemoryDiagnoser]
public class ContainerCostBenchmarks
{
    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    [Params("DATA-03", "DATA-04", "DATA-05", "DATA-07/ascii", "DATA-07/unicode", "DATA-15", "DATA-16", "DATA-19")]
    public string Data { get; set; } = "DATA-03";

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Generated);
        _dataset = Corpus.Find(Data);
        _payload = _dataset.Serialize(_adapter);
    }

    [Benchmark(Description = "write")]
    public byte[] Write() => _dataset.Serialize(_adapter);

    [Benchmark(Description = "read")]
    public object? Read() => _dataset.Deserialize(_adapter, _payload);
}
