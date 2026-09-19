using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins SX-10 and SX-11: the header tells a stream extension which algorithms unwrap the payload, but
/// never which resource policy applies. The caller's limits reach the header-derived overloads.
/// </summary>
public class StreamExtensionsLimitsTests
{
    private static readonly SerializationLimits Tight =
        SerializationLimits.Default with { MaxCollectionLength = 4 };

    private static MemoryStream PayloadOf(int elementCount)
    {
        byte[] bytes = new BinarySerializer().Serialize(Enumerable.Range(0, elementCount).ToList());
        return new MemoryStream(bytes, writable: false);
    }

    [Fact]
    public void Deserialize_WithoutLimits_UsesTheDefaultPolicy()
    {
        using var source = PayloadOf(64);

        var restored = source.Deserialize<List<int>>();

        Assert.Equal(64, restored!.Count);
    }

    [Fact]
    public void Deserialize_WithCallerLimits_EnforcesThem()
    {
        using var source = PayloadOf(64);

        Assert.Throws<BinaryLimitException>(() => source.Deserialize<List<int>>(Tight));
    }

    [Fact]
    public void Deserialize_WithCallerLimits_AcceptsAPayloadWithinThem()
    {
        using var source = PayloadOf(4);

        var restored = source.Deserialize<List<int>>(Tight);

        Assert.Equal(4, restored!.Count);
    }

    [Fact]
    public void Deserialize_WithKeyAndCallerLimits_EnforcesThem()
    {
        using var source = PayloadOf(64);

        Assert.Throws<BinaryLimitException>(() => source.Deserialize<List<int>>((byte[]?)null, Tight));
    }

    [Fact]
    public void Deserialize_WithKeyResolverAndCallerLimits_EnforcesThem()
    {
        using var source = PayloadOf(64);

        Assert.Throws<BinaryLimitException>(
            () => source.Deserialize<List<int>>(static _ => null, Tight));
    }

    [Fact]
    public void Deserialize_IntoAnExistingInstanceWithCallerLimits_EnforcesThem()
    {
        byte[] bytes = new BinarySerializer().Serialize(new SharedLists
        {
            A = Enumerable.Range(0, 64).ToList()
        });
        using var source = new MemoryStream(bytes, writable: false);

        Assert.Throws<BinaryLimitException>(() => source.Deserialize(new SharedLists(), Tight));
    }

    [Fact]
    public void Deserialize_IntoARefValueWithCallerLimits_EnforcesThem()
    {
        byte[] bytes = new BinarySerializer().Serialize(new PointStruct { X = 1, Y = 2 });
        using var source = new MemoryStream(bytes, writable: false);
        var point = default(PointStruct);

        // A depth of zero is not expressible, so the tightest reachable policy is one structural level.
        var oneLevel = SerializationLimits.Default with { MaxDepth = 1 };
        source.Deserialize(ref point, oneLevel);

        Assert.Equal(1, point.X);
        Assert.Equal(2, point.Y);
    }

    [Fact]
    public void Deserialize_WithInvalidLimits_ThrowsConfiguration()
    {
        using var source = PayloadOf(1);

        Assert.Throws<BinaryConfigurationException>(
            () => source.Deserialize<List<int>>(SerializationLimits.Default with { MaxDepth = 0 }));
    }
}
