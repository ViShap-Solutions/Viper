namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Pins RT-58 and RT-59: a <c>Lazy&lt;T&gt;</c> travels as its value, so writing one materializes it
/// and reading one yields an instance that already knows what it will produce.
/// </summary>
public class LazyTests
{
    private static readonly BinarySerializer Serializer = new();

    [Fact]
    public void Deserialize_MaterializedLazy_RestoresTheValue()
    {
        var lazy = new Lazy<int>(() => 42);
        _ = lazy.Value;

        byte[] payload = Serializer.Serialize(lazy);
        var restored = Serializer.Deserialize<Lazy<int>>(payload);

        Assert.Equal(42, restored!.Value);
    }

    [Fact]
    public void Serialize_UnmaterializedLazy_RunsTheFactory()
    {
        int invocations = 0;
        var lazy = new Lazy<int>(() => { invocations++; return 7; });

        byte[] payload = Serializer.Serialize(lazy);

        Assert.Equal(1, invocations);
        Assert.Equal(7, Serializer.Deserialize<Lazy<int>>(payload)!.Value);
    }

    [Fact]
    public void Serialize_LazyWhoseFactoryThrows_PropagatesTheFactoryException()
    {
        var lazy = new Lazy<int>(() => throw new InvalidOperationException("no value"));

        var ex = Assert.Throws<InvalidOperationException>(() => Serializer.Serialize(lazy));

        Assert.Equal("no value", ex.Message);
    }

    [Fact]
    public void Deserialize_Lazy_DefersUntilTheValueIsAsked()
    {
        byte[] payload = Serializer.Serialize(new Lazy<string>(() => "ada"));

        var restored = Serializer.Deserialize<Lazy<string>>(payload);

        Assert.False(restored!.IsValueCreated);
        Assert.Equal("ada", restored.Value);
        Assert.True(restored.IsValueCreated);
    }

    [Fact]
    public void Deserialize_LazyMember_RestoresTheValue()
    {
        var payload = Serializer.Serialize(new Deferred { Value = new Lazy<int>(() => 5) });

        var restored = Serializer.Deserialize<Deferred>(payload);

        Assert.Equal(5, restored!.Value!.Value);
    }

    private sealed class Deferred
    {
        public Lazy<int>? Value { get; set; }
    }
}
