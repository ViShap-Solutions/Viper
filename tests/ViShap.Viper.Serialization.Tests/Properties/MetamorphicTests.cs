using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Properties;

public sealed class MetamorphicTests
{
    [Fact] public void PROP01_PrimitiveClosure() { foreach(var x in new[]{0,1,-1,int.MaxValue,int.MinValue}) Assert.Equal(x,TestHelpers.RoundTrip(x)); }
    [Fact] public void PROP02_CollectionClosure() { var v=new List<int>{1,2,3}; Assert.Equal(v,TestHelpers.RoundTrip(v)); }
    [Fact] public void PROP03_NestedClosure() { var v=new Person{Name="p",Address=new Address{City="c",Street="s"}}; var a=TestHelpers.RoundTrip(v); Assert.Equal(v.Name,a.Name);Assert.Equal(v.Address.City,a.Address.City); }
    [Fact] public void PROP04_DictionaryOrderIsNonContractual() { var a=TestHelpers.RoundTrip(new Dictionary<int,string>{{1,"a"},{2,"b"}}); var b=TestHelpers.RoundTrip(new Dictionary<int,string>{{2,"b"},{1,"a"}}); TestHelpers.AssertDictionary(a,b); }
    [Fact] public void PROP05_ContractOrderIsContractual() { var p=TypeAccessorCache.GetOrBuild(typeof(OrderedMemberFixture)); Assert.Equal(new[]{"A","B","C"},p.Members.Select(x=>x.Name)); }
    [Fact]
    public void PROP06_CorruptionLocalityProducesDeterministicFailure()
    {
        var options = BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build();
        var bytes = new BinarySerializer(options).Serialize(123);
        bytes[^1] ^= 1;

        var exception = Record.Exception(() => new BinarySerializer(options).Deserialize<int>(bytes));
        Assert.IsType<BinaryIntegrityException>(exception);
    }
    [Fact]
    public void PROP07_LimitMonotonicity()
    {
        var baseLimits = new DeserializationLimits
        {
            MaxArrayLength = 4, MaxCollectionLength = 16, MaxDictionaryEntries = 16,
            MaxStringLength = 64, MaxByteBlobLength = 64, MaxTotalElements = 100,
            MaxMessageBytes = 1024, MaxDepth = 10
        };
        var bytes = new BinarySerializer(BinarySerializerOptions.Configure().WithLimits(baseLimits).Build())
            .Serialize(new[] { 1, 2, 3, 4 });

        var lowLimits = baseLimits with { MaxArrayLength = 3 };
        var low = new BinarySerializer(BinarySerializerOptions.Configure().WithLimits(lowLimits).Build());
        var high = new BinarySerializer(BinarySerializerOptions.Configure().WithLimits(baseLimits).Build());

        Assert.Throws<BinaryFormatException>(() => low.Deserialize<int[]>(bytes));
        Assert.Equal(new[] { 1, 2, 3, 4 }, high.Deserialize<int[]>(bytes));
    }
    [Fact] public void PROP08_BudgetMonotonicity() { var b=new DeserializationBudget(new DeserializationLimits{MaxDepth=4,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=3,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=5,MaxMessageBytes=32}); b.ConsumeElements(2); Assert.Throws<BinaryFormatException>(()=>b.ConsumeElements(4)); Assert.Equal(0,b.Depth); }
}
