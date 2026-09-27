using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-23: <see cref="PooledPayload"/> holds exactly the bytes <c>Serialize&lt;T&gt;(T)</c> writes,
/// owns its rented array until it is disposed, may be disposed more than once, and refuses access
/// afterwards.
/// </summary>
public class PooledPayloadTests
{
    private static Person Value => new() { Name = "Ada", Age = 36 };

    public static TheoryData<int> Versions => [1, 0];

    private static BinarySerializer For(int version) =>
        new(BinarySerializerOptions.Configure().WithVersion(version).Build());

    [Theory]
    [MemberData(nameof(Versions))]
    public void SerializePooled_HoldsTheBytesOfTheArrayForm(int version)
    {
        var serializer = For(version);

        using var payload = serializer.SerializePooled(Value);

        Assert.Equal(serializer.Serialize(Value), payload.Span.ToArray());
        Assert.Equal(serializer.Serialize(Value), payload.Memory.ToArray());
    }

    [Fact]
    public void SerializePooled_ALargeValue_HoldsTheBytesOfTheArrayForm()
    {
        var serializer = new BinarySerializer();
        var value = new Person { Name = new string('p', 200_000), Age = 1 };

        using var payload = serializer.SerializePooled(value);

        Assert.Equal(serializer.Serialize(value), payload.Span.ToArray());
    }

    [Fact]
    public void SerializePooled_IsReadBack()
    {
        var serializer = new BinarySerializer();

        using var payload = serializer.SerializePooled(Value);

        Assert.Equal("Ada", serializer.Deserialize<Person>(payload.Span)!.Name);
    }

    [Fact]
    public void Dispose_Twice_IsSafe()
    {
        var payload = new BinarySerializer().SerializePooled(Value);

        payload.Dispose();
        payload.Dispose();
    }

    [Fact]
    public void Access_AfterDispose_ThrowsObjectDisposed()
    {
        var payload = new BinarySerializer().SerializePooled(Value);
        payload.Dispose();

        Assert.Throws<ObjectDisposedException>(() => payload.Memory);
        Assert.Throws<ObjectDisposedException>(() => payload.Span.Length);
    }

    [Fact]
    public void SerializePooled_AValueThatCannotBeEncoded_ThrowsAndHandsOutNothing()
    {
        var tree = new Tree();
        tree.Add(tree);

        Assert.Throws<BinaryTypeException>(() => new BinarySerializer().SerializePooled(tree));
    }
}
