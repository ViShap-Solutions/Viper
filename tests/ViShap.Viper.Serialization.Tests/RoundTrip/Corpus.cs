using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// The round-trip corpus of plan §19, written once and run under every profile that has to carry it:
/// the default V1 frame, the headerless V0 payload (RT-C08) and the full V1 envelope with
/// compression, checksum and authenticated encryption (RT-C09). The corpus never names a profile, so
/// a family that survives one and not another fails here rather than in a profile-specific copy.
/// </summary>
/// <remarks>
/// The corpus is split across several files as partial declarations of this one class: §19.1 in
/// <c>CorpusPrimitives</c>, §19.2 in <c>CorpusTimeAndSystem</c>, §19.3 in <c>CorpusArrays</c>, §19.4
/// in <c>CorpusComposites</c> and §19.5 in <c>CorpusCollections</c>. The profiles are in
/// <c>CorpusProfiles</c>.
/// </remarks>
public abstract partial class Corpus
{
    /// <summary>The serializer of the profile under test.</summary>
    protected abstract BinarySerializer Serializer { get; }

    /// <summary>The same profile with the limits a boundary item needs, and nothing else changed.</summary>
    protected abstract BinarySerializer WithLimits(SerializationLimits limits);

    /// <summary>Writes and reads one value back through the profile under test.</summary>
    protected T? RoundTrip<T>(T value) => Serializer.Deserialize<T>(Serializer.Serialize(value));

    /// <summary>Writes and reads one value back through the profile under test, under tighter limits.</summary>
    protected T? RoundTrip<T>(T value, SerializationLimits limits)
    {
        var serializer = WithLimits(limits);
        return serializer.Deserialize<T>(serializer.Serialize(value));
    }

    /// <summary>Asserts that every value of a family survives the round trip unchanged.</summary>
    protected void AssertRoundTrips<T>(params T[] values)
    {
        foreach (var value in values)
            Assert.Equal(value, RoundTrip(value));
    }
}
