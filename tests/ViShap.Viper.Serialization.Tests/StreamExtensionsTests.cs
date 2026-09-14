using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests;

public class StreamExtensionsTests
{
    private readonly BinarySerializerOptions _options = BinarySerializerOptions.Default;

    #region Reference Type (Person)

    [Fact]
    public void Stream_Serialize_And_Deserialize_Default()
    {
        var source = new Person { Name = "Eve", Age = 22 };
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var result = stream.Deserialize<Person>();

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Serialize_And_Deserialize_WithOptions()
    {
        var source = new Person { Name = "Frank", Age = 28 };
        using var stream = new MemoryStream();

        stream.Serialize(source, _options);
        stream.Position = 0;
        var result = stream.Deserialize<Person>(_options);

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Deserialize_WithKey()
    {
        var source = new Person { Name = "Grace", Age = 31 };
        byte[]? key = null;
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var result = stream.Deserialize<Person>(key);

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Deserialize_WithKeyResolver()
    {
        var source = new Person { Name = "Heidi", Age = 29 };
        Func<string?, byte[]?> resolver = keyId => null;
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var result = stream.Deserialize<Person>(resolver);

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Deserialize_ExistingInstance_ReferenceType()
    {
        var source = new Person { Name = "Ivan", Age = 45 };
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var existing = new Person();
        var result = stream.Deserialize(existing);

        Assert.Same(existing, result);
        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Deserialize_ExistingInstance_WithOptions_ReferenceType()
    {
        var source = new Person { Name = "Judy", Age = 50 };
        using var stream = new MemoryStream();

        stream.Serialize(source, _options);
        stream.Position = 0;
        var existing = new Person();
        var result = stream.Deserialize(existing, _options);

        Assert.Same(existing, result);
        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Deserialize_ExistingInstance_WithKey_ReferenceType()
    {
        var source = new Person { Name = "Mallory", Age = 33 };
        byte[]? key = null;
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var existing = new Person();
        var result = stream.Deserialize(existing, key);

        Assert.Same(existing, result);
        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Stream_Deserialize_ExistingInstance_WithKeyResolver_ReferenceType()
    {
        var source = new Person { Name = "Niaj", Age = 27 };
        Func<string?, byte[]?> resolver = keyId => null;
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var existing = new Person();
        var result = stream.Deserialize(existing, resolver);

        Assert.Same(existing, result);
        Assert.Equivalent(source, result);
    }

    #endregion

    #region Value Type (PointStruct)

    [Fact]
    public void Stream_Serialize_And_Deserialize_ValueType_Default()
    {
        var source = new PointStruct { X = 1, Y = 2 };
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var result = stream.Deserialize<PointStruct>();

        Assert.Equal(source, result);
    }

    [Fact]
    public void Stream_Deserialize_RefExistingInstance_ValueType()
    {
        var source = new PointStruct { X = 5, Y = 10 };
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var existing = new PointStruct();
        stream.Deserialize(ref existing);

        Assert.Equal(source, existing);
    }

    [Fact]
    public void Stream_Deserialize_RefExistingInstance_WithOptions_ValueType()
    {
        var source = new PointStruct { X = 15, Y = 30 };
        using var stream = new MemoryStream();

        stream.Serialize(source, _options);
        stream.Position = 0;
        var existing = new PointStruct();
        stream.Deserialize(ref existing, _options);

        Assert.Equal(source, existing);
    }

    [Fact]
    public void Stream_Deserialize_RefExistingInstance_WithKey_ValueType()
    {
        var source = new PointStruct { X = 50, Y = 60 };
        byte[]? key = null;
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var existing = new PointStruct();
        stream.Deserialize(ref existing, key);

        Assert.Equal(source, existing);
    }

    [Fact]
    public void Stream_Deserialize_RefExistingInstance_WithKeyResolver_ValueType()
    {
        var source = new PointStruct { X = 70, Y = 80 };
        Func<string?, byte[]?> resolver = keyId => null;
        using var stream = new MemoryStream();

        stream.Serialize(source);
        stream.Position = 0;
        var existing = new PointStruct();
        stream.Deserialize(ref existing, resolver);

        Assert.Equal(source, existing);
    }

    #endregion
}