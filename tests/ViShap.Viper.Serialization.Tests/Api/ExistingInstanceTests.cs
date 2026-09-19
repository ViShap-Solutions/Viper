using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-09…API-12: the overloads that read into something the caller already holds. A value type
/// is read as the writer framed it and assigned; a reference type is populated only when its payload
/// really is a member layout.
/// </summary>
public class ExistingInstanceTests
{
    private readonly BinarySerializer _serializer = new();

    private static BinarySerializer WithReferences() =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    [Fact]
    public void Deserialize_RefPrimitive_RestoresTheValue()
    {
        int value = 0;

        _serializer.Deserialize(_serializer.Serialize(123), ref value);

        Assert.Equal(123, value);
    }

    [Fact]
    public void Deserialize_RefStruct_RestoresEveryMember()
    {
        var source = new PointStruct { X = 5, Y = 7 };
        var target = new PointStruct();

        _serializer.Deserialize(_serializer.Serialize(source), ref target);

        Assert.Equal(source, target);
    }

    [Fact]
    public void Deserialize_RefStructWithPreserveReferences_RestoresEveryMember()
    {
        var serializer = WithReferences();
        var source = new PointStruct { X = 5, Y = 7 };
        var target = new PointStruct();

        serializer.Deserialize(serializer.Serialize(source), ref target);

        Assert.Equal(source, target);
    }

    [Fact]
    public void Deserialize_RefStructFromAStream_RestoresEveryMember()
    {
        var source = new PointStruct { X = 5, Y = 7 };
        using var stream = new MemoryStream(_serializer.Serialize(source), writable: false);
        var target = new PointStruct();

        _serializer.Deserialize(stream, ref target);

        Assert.Equal(source, target);
    }

    [Fact]
    public void Deserialize_PopulateObjectInPlace_ReusesTheInstance()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 30 });
        var target = new Person();

        var result = _serializer.Deserialize(payload, target);

        Assert.Same(target, result);
        Assert.Equal("Alice", target.Name);
        Assert.Equal(30, target.Age);
    }

    [Fact]
    public void Deserialize_PopulateObjectInPlaceFromAStream_ReusesTheInstance()
    {
        using var stream = new MemoryStream(
            _serializer.Serialize(new Person { Name = "Alice", Age = 30 }), writable: false);
        var target = new Person();

        var result = _serializer.Deserialize(stream, target);

        Assert.Same(target, result);
        Assert.Equal("Alice", target.Name);
    }

    [Fact]
    public void Deserialize_PopulateObjectInPlaceTwice_OverwritesEveryMember()
    {
        byte[] first = _serializer.Serialize(new Person { Name = "Alice", Age = 30 });
        byte[] second = _serializer.Serialize(new Person { Name = "Bob", Age = 41 });
        var target = new Person();

        _serializer.Deserialize(first, target);
        _serializer.Deserialize(second, target);

        Assert.Equal("Bob", target.Name);
        Assert.Equal(41, target.Age);
    }

    [Fact]
    public void Deserialize_PopulateCollectionInPlace_ThrowsType()
    {
        // A collection's payload is not a member layout, so it must not be reinterpreted as one.
        byte[] payload = _serializer.Serialize(new List<int> { 1, 2, 3 });

        Assert.Throws<BinaryTypeException>(
            () => _serializer.Deserialize(payload, new List<int>()));
    }

    [Fact]
    public void Deserialize_PopulateWithANullInstance_ThrowsArgumentNull()
    {
        byte[] payload = _serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize(payload, (Person)null!));
    }
}
