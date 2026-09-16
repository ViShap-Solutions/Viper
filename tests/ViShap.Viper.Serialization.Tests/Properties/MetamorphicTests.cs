using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Properties;

public sealed class MetamorphicTests
{
    [Fact] public void PROP01_PrimitiveClosure() { foreach(var x in new[]{0,1,-1,int.MaxValue,int.MinValue}) Assert.Equal(x,TestHelpers.RoundTrip(x)); }
    [Fact] public void PROP02_CollectionClosure() { var v=new List<int>{1,2,3}; Assert.Equal(v,TestHelpers.RoundTrip(v)); }
    [Fact] public void PROP03_NestedClosure() { var v=new Person{Name="p",Address=new Address{City="c",Street="s"}}; var a=TestHelpers.RoundTrip(v); Assert.Equal(v.Name,a.Name);Assert.Equal(v.Address.City,a.Address.City); }
    [Fact] public void PROP04_DictionaryOrderIsNonContractual() { var a=TestHelpers.RoundTrip(new Dictionary<int,string>{{1,"a"},{2,"b"}}); var b=TestHelpers.RoundTrip(new Dictionary<int,string>{{2,"b"},{1,"a"}}); TestHelpers.AssertDictionary(a,b); }
    [Fact] public void PROP05_ContractOrderIsContractual() { var p=TypeAccessorCache.GetOrBuild(typeof(OrderedMemberFixture)); Assert.Equal(new[]{"A","B","C"},p.Members.Select(x=>x.Name)); }
    [Fact] public void PROP06_CorruptionLocalityProducesDeterministicFailure() { var b=new BinarySerializer().Serialize(123); b[^1]^=1; var e=Record.Exception(()=>new BinarySerializer().Deserialize<int>(b)); Assert.NotNull(e); Assert.True(e is BinaryFormatException or BinaryIntegrityException); }
    [Fact] public void PROP07_LimitMonotonicity() { var p=new BinarySerializer(BinarySerializerOptions.Configure().WithLimits(new DeserializationLimits{MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=3,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=100,MaxMessageBytes=1024,MaxDepth=10}).Build()); var b=p.Serialize(new[]{1,2,3,4}); var low=new BinarySerializer(TestHelpers.TightOptions()); Assert.Throws<BinaryFormatException>(()=>low.Deserialize<int[]>(b)); Assert.Equal(new[]{1,2,3,4},p.Deserialize<int[]>(b)); }
    [Fact] public void PROP08_BudgetMonotonicity() { var b=new DeserializationBudget(new DeserializationLimits{MaxDepth=4,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=3,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=5,MaxMessageBytes=32}); b.ConsumeElements(2); Assert.Throws<BinaryFormatException>(()=>b.ConsumeElements(4)); Assert.Equal(0,b.Depth); }
}
