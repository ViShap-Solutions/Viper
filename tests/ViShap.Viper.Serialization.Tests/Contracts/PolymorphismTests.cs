using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins PM-01 and PM-08…PM-10: only a declared union may carry a runtime type that differs from the
/// declared one. Without a map the write is refused, because the reader could not reconstruct it.
/// </summary>
public class PolymorphismTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Deserialize_DerivedValueThroughDeclaredUnion_KeepsTheRuntimeType()
    {
        var source = new UnionDerived { A = 11, Z = 22 };

        var result = _serializer.Deserialize<UnionBase>(_serializer.Serialize<UnionBase>(source));

        var derived = Assert.IsType<UnionDerived>(result);
        Assert.Equal(11, derived.A);
        Assert.Equal(22, derived.Z);
    }

    [Fact]
    public void Deserialize_UnionInsideACollection_KeepsTheRuntimeType()
    {
        var source = new List<UnionBase>
        {
            new UnionDerived { A = 1, Z = 2 },
            new UnionDerived { A = 3, Z = 4 }
        };

        var result = _serializer.Deserialize<List<UnionBase>>(_serializer.Serialize(source))!;

        Assert.Equal(1, Assert.IsType<UnionDerived>(result[0]).A);
        Assert.Equal(3, Assert.IsType<UnionDerived>(result[1]).A);
    }

    [Fact]
    public void Deserialize_RegisteredConcreteBase_KeepsTheRuntimeType()
    {
        // A map covers every runtime type that may appear, the base included: a tag always precedes
        // the members, so an untagged base has no representation.
        var source = new List<TaggedBase> { new() { Z = 1 }, new TaggedDerived { A = 2, Z = 3 } };

        var result = _serializer.Deserialize<List<TaggedBase>>(_serializer.Serialize(source))!;

        Assert.Equal(typeof(TaggedBase), result[0].GetType());
        Assert.Equal(2, Assert.IsType<TaggedDerived>(result[1]).A);
    }

    [Fact]
    public void Serialize_BaseTypeAbsentFromItsOwnUnionMap_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(new UnionBase { Z = 1 }));
    }

    [Fact]
    public void Deserialize_UnionInsideADictionaryValue_KeepsTheRuntimeType()
    {
        var source = new Dictionary<string, UnionBase>
        {
            ["derived"] = new UnionDerived { A = 1, Z = 2 }
        };

        var result = _serializer.Deserialize<Dictionary<string, UnionBase>>(
            _serializer.Serialize(source))!;

        Assert.IsType<UnionDerived>(result["derived"]);
    }

    [Fact]
    public void Serialize_DerivedValueThroughBaseWithoutUnion_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize<Base>(new Derived { A = 11, Z = 22 }));
    }

    [Fact]
    public void Serialize_ValueThroughObjectWithoutUnion_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize<object>(new Person { Name = "Alice", Age = 30 }));
    }

    [Fact]
    public void Serialize_DerivedValueInsideCollectionWithoutUnion_ThrowsType()
    {
        Assert.Throws<BinaryTypeException>(
            () => _serializer.Serialize(new List<Base> { new Derived { A = 1, Z = 2 } }));
    }

    [Fact]
    public void Serialize_DeclaredTypeMatchingTheRuntimeType_Succeeds()
    {
        // The write-side rejection is about a mismatch, not about inheritance existing.
        byte[] payload = _serializer.Serialize(new Base { Z = 5 });

        Assert.Equal(5, _serializer.Deserialize<Base>(payload)!.Z);
    }
}
