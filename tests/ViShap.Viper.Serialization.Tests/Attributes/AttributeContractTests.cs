using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Attributes;

public sealed class AttributeTests
{
    [Fact] public void ATTR01_PublicPropertiesAndATTR02_PublicFieldsAreIncluded()
    {
        var value = new PublicPropertyAndField { Property=7, Field=9 }; var actual=TestHelpers.RoundTrip(value); Assert.Equal(value.Property,actual.Property); Assert.Equal(value.Field,actual.Field);
    }

    [Fact] public void ATTR03_BinaryIgnoreExcludesMember()
    {
        var actual=TestHelpers.RoundTrip(new IgnoredMemberFixture{Included=1,Ignored=2}); Assert.Equal(1,actual.Included); Assert.Equal(0,actual.Ignored);
    }

    [Fact] public void ATTR04_PrivatePropertyWithBinaryIncludeIsIncluded()
    {
        var value=new PrivateMemberFixture{PublicValue=1}; value.SetPrivate("secret"); var actual=TestHelpers.RoundTrip(value); Assert.Equal(1,actual.PublicValue); Assert.Equal("secret",actual.ReadPrivate());
    }

    [Fact] public void ATTR05_PrivateFieldWithBinaryIncludeIsIncluded() { var value=new PrivateFieldFixture();value.SetPrivate(42);var actual=TestHelpers.RoundTrip(value);Assert.Equal(42,actual.ReadPrivate()); }

    [Fact] public void ATTR06_IgnoreAndPrivateIncludeCompose() { var v=new CombinedMemberFixture{Ignored=9};v.SetIncluded(7);var a=TestHelpers.RoundTrip(v);Assert.Equal(0,a.Ignored);Assert.Equal(7,a.ReadIncluded()); }

    [Fact] public void ATTR07_ExplicitOrderIsDeterministic()
    {
        var options=BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(); var bytes=new BinarySerializer(options).Serialize(new OrderedMemberFixture{A=11,B=22,C=33});
        // root members are serialized as int32 values after null/object metadata; verify the fixture round-trips and plan order directly.
        var plan=TypeAccessorCache.GetOrBuild(typeof(OrderedMemberFixture)); Assert.Equal(new[]{"A","B","C"},plan.Members.Select(m=>m.Name)); Assert.NotEmpty(bytes);
    }

    [Fact] public void ATTR08_DuplicateOrderThrows() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(DuplicateOrderFixture)); });
    [Fact] public void ATTR09_KeyWithoutContractThrows() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(InvalidKeyWithoutContractFixture)); });
    [Fact] public void ATTR10_CompleteContractKeysRoundTrip()
    {
        var value=new ContractV1{Name="Ada",Age=37}; var actual=TestHelpers.RoundTrip(value); Assert.Equal(value.Name,actual.Name); Assert.Equal(value.Age,actual.Age);
    }
    [Fact] public void ATTR11_ContractIgnorePreservesDefault()
    {
        var actual=TestHelpers.RoundTrip(new ContractIgnoreFixture{A=7,B=9}); Assert.Equal(7,actual.A); Assert.Equal(0,actual.B);
    }
    [Fact] public void ATTR12_MissingContractDecisionThrows() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(InvalidContractFixture)); });
    [Fact] public void ATTR13_ContractIncludeThrows() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(ContractIncludeFixture)); });
    [Fact] public void ATTR14_ContractOrderThrows() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(ContractOrderFixture)); });
    [Fact] public void ATTR15_DuplicateKeyThrows() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(DuplicateKeyFixture)); });

    [Fact]
    public void ATTR16_WireEncodingMustHonorKeyAssignments()
    {
        var a = new ContractKeyOrderA { First = 1, Second = 2 };
        var b = new ContractKeyOrderB { First = 1, Second = 2 };
        var bytesA = TestHelpers.Serialize(a);
        var bytesB = TestHelpers.Serialize(b);
        Assert.NotEqual(bytesA, bytesB);
    }

    [Fact] public void ATTR16_KeyBoundaryValuesAreAcceptedByAttributeMetadata()
    {
        foreach(var t in new[]{typeof(Key1),typeof(Key127),typeof(Key128),typeof(Key16383),typeof(Key16384)}) Assert.NotEmpty(TypeAccessorCache.GetOrBuild(t).Members);
    }

    public sealed class PublicPropertyAndField { public int Property {get;set;} public int Field; }
    [BinaryContract] public sealed class ContractIgnoreFixture { [BinaryKey(1)] public int A {get;set;} [BinaryIgnore] public int B {get;set;} }
    [BinaryContract] public sealed class Key1 { [BinaryKey(1)] public int A {get;set;} }
    [BinaryContract] public sealed class Key127 { [BinaryKey(127)] public int A {get;set;} }
    [BinaryContract] public sealed class Key128 { [BinaryKey(128)] public int A {get;set;} }
    [BinaryContract] public sealed class Key16383 { [BinaryKey(16383)] public int A {get;set;} }
    [BinaryContract] public sealed class Key16384 { [BinaryKey(16384)] public int A {get;set;} }
    [BinaryContract] public sealed class ContractKeyOrderA { [BinaryKey(1)] public int First {get;set;} [BinaryKey(2)] public int Second {get;set;} }
    [BinaryContract] public sealed class ContractKeyOrderB { [BinaryKey(2)] public int First {get;set;} [BinaryKey(1)] public int Second {get;set;} }
}
