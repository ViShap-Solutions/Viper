using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Scenarios;

[MemoryDiagnoser]
[BenchmarkCategory("Streaming","Warm")]
public class StreamingBenchmarks
{
    [Params(DataSetKind.TinyFlat,DataSetKind.CollectionMedium,DataSetKind.CollectionLarge)] public DataSetKind DataSet {get;set;}
    [Params("Viper","System.Text.Json","protobuf-net","MessagePack","Orleans.Serialization","XmlSerializer")] public string Library {get;set;}="Viper";
    private BenchmarkPayload _value=null!; private byte[] _payload=null!; private IStreamBenchmarkSerializer _adapter=null!;
    [GlobalSetup] public void Setup(){_value=BenchmarkDataSet.Create(DataSet);_adapter=Program.CreateStreamAdapter(Library);using var s=new MemoryStream();_adapter.Serialize(s,_value);_payload=s.ToArray();}
    [Benchmark] public long SerializeStream(){using var s=new MemoryStream();_adapter.Serialize(s,_value);return s.Length;}
    [Benchmark] public BenchmarkPayload DeserializeStream(){using var s=new MemoryStream(_payload);return _adapter.Deserialize<BenchmarkPayload>(s);}
}
