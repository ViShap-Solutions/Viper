using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.API;

public class BinarySerializerApiTests
{
    private readonly BinarySerializer _serializer = new();

    #region Reference Type (Person)

    [Fact]
    public void Serialize_Deserialize_ByteArray_ReferenceType()
    {
        var source = new Person { Name = "Alice", Age = 30 };

        byte[] bytes = _serializer.Serialize(source);
        var result = _serializer.Deserialize<Person>(bytes);

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Serialize_Deserialize_Stream_ReferenceType()
    {
        var source = new Person { Name = "Bob", Age = 25 };
        using var stream = new MemoryStream();

        _serializer.Serialize(stream, source);
        stream.Position = 0;
        var result = _serializer.Deserialize<Person>(stream);

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Deserialize_ByteArray_IntoExistingInstance_ReferenceType()
    {
        var source = new Person { Name = "Charlie", Age = 40 };
        byte[] bytes = _serializer.Serialize(source);

        var existing = new Person();
        var result = _serializer.Deserialize(bytes, existing);

        Assert.Same(existing, result);
        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Deserialize_Stream_IntoExistingInstance_ReferenceType()
    {
        var source = new Person { Name = "Dave", Age = 35 };
        using var stream = new MemoryStream();
        _serializer.Serialize(stream, source);
        stream.Position = 0;

        var existing = new Person();
        var result = _serializer.Deserialize(stream, existing);

        Assert.Same(existing, result);
        Assert.Equivalent(source, result);
    }

    #endregion

    #region Value Type (PointStruct)

    [Fact]
    public void Serialize_Deserialize_ByteArray_ValueType()
    {
        var source = new PointStruct { X = 10, Y = 20 };

        byte[] bytes = _serializer.Serialize(source);
        var result = _serializer.Deserialize<PointStruct>(bytes);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Serialize_Deserialize_Stream_ValueType()
    {
        var source = new PointStruct { X = 15, Y = 25 };
        using var stream = new MemoryStream();

        _serializer.Serialize(stream, source);
        stream.Position = 0;
        var result = _serializer.Deserialize<PointStruct>(stream);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Deserialize_ByteArray_RefExistingInstance_ValueType()
    {
        var source = new PointStruct { X = 100, Y = 200 };
        byte[] bytes = _serializer.Serialize(source);

        var existing = new PointStruct();
        _serializer.Deserialize(bytes, ref existing);

        Assert.Equal(source, existing);
    }

    [Fact]
    public void Deserialize_Stream_RefExistingInstance_ValueType()
    {
        var source = new PointStruct { X = 300, Y = 400 };
        using var stream = new MemoryStream();
        _serializer.Serialize(stream, source);
        stream.Position = 0;

        var existing = new PointStruct();
        _serializer.Deserialize(stream, ref existing);

        Assert.Equal(source, existing);
    }

    #endregion
}