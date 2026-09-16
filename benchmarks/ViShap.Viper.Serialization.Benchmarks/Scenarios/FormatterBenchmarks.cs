using BenchmarkDotNet.Attributes;
using ViShap.Viper;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Scenarios;

[MemoryDiagnoser]
[BenchmarkCategory("Formatter")]
public class FormatterBenchmarks
{
    private readonly BinarySerializer _serializer=new();
    private readonly int _primitive=123456789;
    private readonly string _string=new string('x',256);
    private readonly int[] _array=Enumerable.Range(0,1000).ToArray();
    private readonly List<int> _list=Enumerable.Range(0,1000).ToList();
    private readonly Dictionary<int,string> _dictionary=Enumerable.Range(0,1000).ToDictionary(i=>i,i=>$"v{i}");
    private readonly BenchmarkPayload _nested=BenchmarkDataSet.Create(DataSetKind.TinyFlat);
    [Benchmark] public byte[] Primitive()=>_serializer.Serialize(_primitive);
    [Benchmark] public byte[] String()=>_serializer.Serialize(_string);
    [Benchmark] public byte[] Array()=>_serializer.Serialize(_array);
    [Benchmark] public byte[] List()=>_serializer.Serialize(_list);
    [Benchmark] public byte[] Dictionary()=>_serializer.Serialize(_dictionary);
    [Benchmark] public byte[] ImmutableList()=>_serializer.Serialize(System.Collections.Immutable.ImmutableList.CreateRange(Enumerable.Range(0,1000)));
    [Benchmark] public byte[] Polymorphic()=>_serializer.Serialize(new PolymorphicBenchmarkValue { Value = new PolymorphicBenchmarkDog { Name = "dog", BarkVolume = 9 } });
    [Benchmark] public byte[] NestedPoco()=>_serializer.Serialize(_nested);
}

[MemoryDiagnoser]
[BenchmarkCategory("Allocation")]
public class AllocationBenchmarks
{
    private readonly BinarySerializer _serializer=new();
    private readonly List<int> _values=Enumerable.Range(0,100_000).ToList();
    [Benchmark] public byte[] LargeListSerialize()=>_serializer.Serialize(_values);
    [Benchmark] public List<int> LargeListRoundTrip(){var b=_serializer.Serialize(_values);return _serializer.Deserialize<List<int>>(b)!;}
}

[ViShap.Viper.BinaryUnion(1, typeof(PolymorphicBenchmarkDog))]
public abstract class PolymorphicBenchmarkAnimal { public string Name { get; set; } = ""; }
public sealed class PolymorphicBenchmarkDog : PolymorphicBenchmarkAnimal { public int BarkVolume { get; set; } }
public sealed class PolymorphicBenchmarkValue { public PolymorphicBenchmarkAnimal Value { get; set; } = null!; }
