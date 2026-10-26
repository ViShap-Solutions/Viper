using BenchmarkDotNet.Attributes;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-19 — the dumper, informational: <c>Dump&lt;T&gt;</c> of a tiny record, a large batch of
/// records and a large byte array, time and allocation. It gates nothing — diagnostics is not a hot
/// path — but it states what a dump of a large frame costs, and a rendering that grows faster than
/// the number of nodes shows here.
/// </summary>
[MemoryDiagnoser]
public class DumperBenchmarks
{
    private byte[] _tiny = [];
    private byte[] _batch = [];
    private byte[] _blob = [];

    [GlobalSetup]
    public void Setup()
    {
        var serializer = new BinarySerializer();
        _tiny = serializer.Serialize(((Dataset<TinyFlat>)Corpus.Find("DATA-01")).Value);
        _batch = serializer.Serialize(((Dataset<List<TinyFlat>>)Corpus.Find("DATA-04")).Value);
        _blob = serializer.Serialize(((Dataset<BlobEnvelope>)Corpus.Find("DATA-08")).Value);
    }

    [Benchmark(Description = "MICRO-19 dump DATA-01")]
    public int Tiny() => BinaryFormatDumper.Dump<TinyFlat>(_tiny).NodeCount;

    [Benchmark(Description = "MICRO-19 dump DATA-04")]
    public int Batch() => BinaryFormatDumper.Dump<List<TinyFlat>>(_batch).NodeCount;

    [Benchmark(Description = "MICRO-19 dump DATA-08")]
    public int Blob() => BinaryFormatDumper.Dump<BlobEnvelope>(_blob).NodeCount;
}
