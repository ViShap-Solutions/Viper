using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Scenarios;

[MemoryDiagnoser]
[BenchmarkCategory("ColdStart")]
public class ColdStartBenchmarks
{
    [Params("Viper","System.Text.Json","System.Text.Json.SourceGen","protobuf-net","MessagePack","Orleans","ZeroFormatter","MemoryPack","XmlSerializer")] public string Library {get;set;}="Viper";
    [Benchmark] public byte[] FirstSerialize(){var adapter=Program.CreateAdapter(Library);try{return adapter.Serialize(BenchmarkDataSet.Create(DataSetKind.TinyFlat));}finally{(adapter as IDisposable)?.Dispose();}}
}

[MemoryDiagnoser]
[BenchmarkCategory("ColdStart")]
public class ColdStartDeserializeBenchmarks
{
    [Params("Viper","System.Text.Json","System.Text.Json.SourceGen","protobuf-net","MessagePack","Orleans","ZeroFormatter","MemoryPack","XmlSerializer")] public string Library {get;set;}="Viper";
    private byte[] _payload=null!;
    [GlobalSetup] public void Setup(){var adapter=Program.CreateAdapter(Library);try{_payload=adapter.Serialize(BenchmarkDataSet.Create(DataSetKind.TinyFlat));}finally{(adapter as IDisposable)?.Dispose();}}
    [Benchmark] public BenchmarkPayload FirstDeserialize(){var adapter=Program.CreateAdapter(Library);try{return adapter.Deserialize<BenchmarkPayload>(_payload);}finally{(adapter as IDisposable)?.Dispose();}}
}
