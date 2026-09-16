using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Polymorphism;

public sealed class PolymorphismTests
{
    [Fact] public void PM01_RegisteredDerivedClassPreservesRuntimeType()
    {
        Animal actual=TestHelpers.RoundTrip<Animal>(new Dog{Name="Rex",BarkVolume=8}); Assert.IsType<Dog>(actual); Assert.Equal(8,((Dog)actual).BarkVolume);
    }

    [Fact] public void PM02_MultipleRegisteredDerivedClassesPreserveRuntimeTypes()
    {
        var actual=TestHelpers.RoundTrip<Animal[]>(new Animal[]{new Dog{Name="d",BarkVolume=1},new Cat{Name="c",Lives=9}});
        Assert.IsType<Dog>(actual[0]); Assert.IsType<Cat>(actual[1]);
    }

    [Fact] public void PM03_ConcreteBaseMayBeSerializedWithoutUnionDiscriminator()
    {
        var value=new ConcreteAnimal{Name="base"}; var actual=TestHelpers.RoundTrip(value); Assert.IsType<ConcreteAnimal>(actual); Assert.Equal(value.Name,actual.Name);
    }

    [Fact] public void PM04_UnionInsideCollection() => PM02_MultipleRegisteredDerivedClassesPreserveRuntimeTypes();
    [Fact] public void PM05_UnionInsideDictionary()
    {
        var value=new Dictionary<string,Animal>{{"dog",new Dog{Name="d",BarkVolume=3}},{"cat",new Cat{Name="c",Lives=7}}}; var actual=TestHelpers.RoundTrip(value);
        Assert.IsType<Dog>(actual["dog"]); Assert.IsType<Cat>(actual["cat"]);
    }
    [Fact] public void PM06_UnionInsideKeyedContractMember()
    {
        var value=new ContractWithAnimal{Animal=new Dog{Name="d",BarkVolume=4}}; var actual=TestHelpers.RoundTrip(value); Assert.IsType<Dog>(actual.Animal);
    }
    [Fact] public void PM07_UnknownDiscriminatorIsRejected()
    {
        var v0=BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(); var payload=new BinarySerializer(v0).Serialize<Animal>(new Dog{Name="d"});
        Assert.True(payload.Length>1); payload[1]=99;
        Assert.Throws<BinaryTypeException>(() => { new BinarySerializer(v0).Deserialize<Animal>(payload); });
    }
    [Fact] public void PM08_UnregisteredRuntimeTypeFailsOnWrite() => Assert.Throws<BinaryTypeException>(() => { new BinarySerializer().Serialize<Animal>(new UnregisteredAnimal{Name="u"}); });
    [Fact] public void PM09_DuplicateUnionTagFailsDuringMapConstruction() => Assert.Throws<BinaryTypeException>(() => { PolymorphicTypeCache.GetMap(typeof(DuplicateTagBase)); });
    [Fact] public void PM10_TagOutsideByteRangeFails() => Assert.Throws<BinaryTypeException>(() => { PolymorphicTypeCache.GetMap(typeof(OutOfRangeTagBase)); });
    [Fact] public void PM11_NonAssignableDerivedTypeFails() => Assert.Throws<BinaryTypeException>(() => { PolymorphicTypeCache.GetMap(typeof(InvalidAssignableBase)); });
    [Fact] public async Task PM12_ConcurrentFirstTouchIsDeterministic()
    {
        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => PolymorphicTypeCache.GetMap(typeof(Animal))))
            .ToArray();

        var maps = await Task.WhenAll(tasks);
        Assert.All(maps, map => Assert.NotNull(map));
        Assert.All(maps, map => Assert.True(map!.TryGetType(1, out var type) && type == typeof(Dog)));
    }

    public class ConcreteAnimal { public string Name {get;set;}=""; }
    public sealed class UnregisteredAnimal : Animal { }
    [BinaryContract] public sealed class ContractWithAnimal { [BinaryKey(1)] public Animal Animal {get;set;}=null!; }
    [BinaryUnion(1,typeof(Dog))][BinaryUnion(1,typeof(Cat))] public abstract class DuplicateTagBase { }
    [BinaryUnion(256,typeof(Dog))] public abstract class OutOfRangeTagBase { }
    [BinaryUnion(1,typeof(string))] public abstract class InvalidAssignableBase { }
}
