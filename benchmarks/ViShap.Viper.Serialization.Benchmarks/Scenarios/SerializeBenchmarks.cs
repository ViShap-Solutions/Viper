using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Scenarios;

[MemoryDiagnoser]
[BenchmarkCategory("Serialize","Warm")]
public class SerializeBenchmarks
{
    [Params(DataSetKind.TinyFlat,DataSetKind.MediumFlat,DataSetKind.CollectionMedium,DataSetKind.CollectionLarge,DataSetKind.DictionaryHeavy,DataSetKind.DeepGraph,DataSetKind.UnicodeHeavy,DataSetKind.Random,DataSetKind.Compressible)] public DataSetKind DataSet {get;set;}
    [Params("Viper","System.Text.Json","System.Text.Json.SourceGen","protobuf-net","MessagePack","Orleans","ZeroFormatter","MemoryPack","XmlSerializer")] public string Library {get;set;}="Viper";
    private BenchmarkPayload _value=null!; private IBenchmarkSerializer _adapter=null!;
    [GlobalSetup] public void Setup(){_value=BenchmarkDataSet.Create(DataSet);_adapter=Program.CreateAdapter(Library);if(!_adapter.IsSupported)throw new InvalidOperationException($"{Library} does not support {DataSet}");_adapter.Serialize(_value);}
    [Benchmark] public byte[] Serialize()=>_adapter.Serialize(_value);
}

[MemoryDiagnoser]
[BenchmarkCategory("Deserialize","Warm")]
public class DeserializeBenchmarks
{
    [Params(DataSetKind.TinyFlat,DataSetKind.MediumFlat,DataSetKind.CollectionMedium,DataSetKind.CollectionLarge,DataSetKind.DictionaryHeavy,DataSetKind.DeepGraph,DataSetKind.UnicodeHeavy,DataSetKind.Random,DataSetKind.Compressible)] public DataSetKind DataSet {get;set;}
    [Params("Viper","System.Text.Json","System.Text.Json.SourceGen","protobuf-net","MessagePack","Orleans","ZeroFormatter","MemoryPack","XmlSerializer")] public string Library {get;set;}="Viper";
    private byte[] _payload=null!; private IBenchmarkSerializer _adapter=null!;
    [GlobalSetup] public void Setup(){_adapter=Program.CreateAdapter(Library);_payload=_adapter.Serialize(BenchmarkDataSet.Create(DataSet));}
    [Benchmark] public BenchmarkPayload Deserialize()=>_adapter.Deserialize<BenchmarkPayload>(_payload);
}

[MemoryDiagnoser]
[BenchmarkCategory("RoundTrip","Warm")]
public class RoundTripBenchmarks
{
    [Params(DataSetKind.TinyFlat,DataSetKind.MediumFlat,DataSetKind.CollectionMedium,DataSetKind.CollectionLarge,DataSetKind.DictionaryHeavy,DataSetKind.DeepGraph,DataSetKind.UnicodeHeavy)] public DataSetKind DataSet {get;set;}
    [Params("Viper","System.Text.Json","System.Text.Json.SourceGen","protobuf-net","MessagePack","Orleans","ZeroFormatter","MemoryPack","XmlSerializer")] public string Library {get;set;}="Viper";
    private BenchmarkPayload _value=null!; private IBenchmarkSerializer _adapter=null!;
    [GlobalSetup] public void Setup(){_value=BenchmarkDataSet.Create(DataSet);_adapter=Program.CreateAdapter(Library);}
    [Benchmark] public BenchmarkPayload RoundTrip(){var bytes=_adapter.Serialize(_value);return _adapter.Deserialize<BenchmarkPayload>(bytes);}
}
