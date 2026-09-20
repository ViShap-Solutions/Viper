using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-06…API-08: an empty array is not a payload in any wire version, so every byte-array entry
/// point rejects it rather than inventing a result.
/// </summary>
public class EmptyPayloadTests
{
    private static readonly BinarySerializer Serializer = new();

    [Fact]
    public void Deserialize_EmptyArray_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(() => Serializer.Deserialize<Person>([]));
    }

    [Fact]
    public void Deserialize_EmptyArrayForAValueType_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(() => Serializer.Deserialize<int>([]));
    }

    [Fact]
    public void Deserialize_EmptyArrayIntoAnExistingInstance_ThrowsFormatAndLeavesItUntouched()
    {
        var person = new Person { Name = "Ada", Age = 36 };

        Assert.Throws<BinaryFormatException>(() => Serializer.Deserialize([], person));

        Assert.Equal("Ada", person.Name);
        Assert.Equal(36, person.Age);
    }

    [Fact]
    public void Deserialize_EmptyArrayIntoARefValue_ThrowsFormatAndLeavesItUntouched()
    {
        var point = new PointStruct { X = 3, Y = 4 };

        Assert.Throws<BinaryFormatException>(() => Serializer.Deserialize([], ref point));

        Assert.Equal(3, point.X);
        Assert.Equal(4, point.Y);
    }

    [Fact]
    public void Deserialize_NullArray_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => Serializer.Deserialize<Person>((byte[])null!));
    }
}
