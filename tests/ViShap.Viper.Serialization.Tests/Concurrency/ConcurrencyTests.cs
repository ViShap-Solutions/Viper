using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Concurrency;

public sealed class ConcurrencyTests
{
    [Fact] public void CN01_SharedSerializerSupports100ConcurrentOperations()
    {
        var serializer=new BinarySerializer(); Parallel.For(0, 128, (int i) =>{var value=new Person{Name=$"p{i}",Age=i,Address=new Address{City="c",Street="s"}};var bytes=serializer.Serialize(value);var actual=serializer.Deserialize<Person>(bytes);Assert.Equal(value.Name,actual!.Name);Assert.Equal(value.Age,actual.Age);});
    }
    [Fact] public void CN02_TypeAccessorFirstTouchProducesSingleCachedPlan()
    {
        var before=TypeAccessorCache.CachedTypeCount; Parallel.For(0, 50, (int _) =>Assert.NotEmpty(TypeAccessorCache.GetOrBuild(typeof(FirstTouchFixture)).Members)); Assert.Equal(before+1,TypeAccessorCache.CachedTypeCount);
    }
    [Fact] public void CN03_PolymorphicFirstTouchIsDeterministic()
    {
        Parallel.For(0, 50, (int _) =>{var map=PolymorphicTypeCache.GetMap(typeof(Animal));Assert.True(map!.TryGetType(1,out var type));Assert.Equal(typeof(Dog),type);});
    }
    [Fact] public void CN04_FormatterFirstTouchProducesUsableResults()
    {
        Parallel.For(0, 50, (int _) =>{var f=TypeFormatterRegistry.Resolve(typeof(Guid));Assert.True(f.CanHandle(typeof(Guid)));});
    }
    [Fact] public void CN05_AccessorCachesSurviveConcurrentFirstTouch()
    {
        Parallel.Invoke(
            () => Assert.NotNull(FrozenFactoryCache.GetToFrozenDictionary(typeof(int),typeof(string))),
            () => Assert.NotNull(ImmutableFactoryCache.GetArrayFactory(typeof(ImmutableCollectionsMarshal), "AsImmutableArray", typeof(int))),
            () => Assert.NotNull(TupleAccessorCache.GetAccessors(typeof((int,int)))),
            () => Assert.NotNull(ReadOnlySequenceAccessorCache.GetWriter(typeof(int))),
            () => Assert.NotNull(DictionaryAccessorCache.GetEntryAccessors(typeof(KeyValuePair<int,string>))));
    }
    [Fact] public void CN06_RegistryConcurrentCustomRegistrationAndResolution()
    {
        Parallel.For(0, 100, (int i) =>{var name=$"qa-{i}";ChecksumAlgorithmRegistry.RegisterCustom(name,()=>new ConcurrencyChecksum(name));var options=BinarySerializerOptions.Configure().WithChecksum(new ConcurrencyChecksum(name)).Build();var bytes=new BinarySerializer(options).Serialize(i);Assert.Equal(i,new BinarySerializer(options).Deserialize<int>(bytes));});
    }
    [Fact] public void CN07_DifferentSerializerInstancesRemainIsolated()
    {
        var key=new byte[32]; var plain=new BinarySerializer(); var encrypted=new BinarySerializer(BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),key,"k").Build()); var plainBytes=plain.Serialize(1); var encryptedBytes=encrypted.Serialize(2); Assert.Equal(1,plain.Deserialize<int>(plainBytes)); Assert.Equal(2,encrypted.Deserialize<int>(encryptedBytes)); Assert.Throws<BinaryIntegrityException>(()=>plain.Deserialize<int>(encryptedBytes));
    }

    private sealed class FirstTouchFixture { public int Value {get;set;} }
}

internal sealed class ConcurrencyChecksum(string name) : IChecksumAlgorithm
{
    public ChecksumAlgorithm Kind=>ChecksumAlgorithm.Custom; public string? CustomName=>name; public int HashSizeInBytes=>1; public void Compute(ReadOnlySpan<byte> source,Span<byte> destination){destination[0]=0;foreach(var b in source)destination[0]^=b;}
}
