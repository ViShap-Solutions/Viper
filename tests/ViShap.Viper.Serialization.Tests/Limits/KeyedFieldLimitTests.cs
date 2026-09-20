using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-20, LIM-21 and LIM-23: the per-object field count and the operation-wide metadata budget
/// are separate limits, and the per-object one is evaluated first, so a caller always learns which
/// ceiling the payload actually broke.
/// </summary>
public class KeyedFieldLimitTests
{
    private static BinarySerializer With(int perObject, long cumulative) =>
        new(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with
            {
                MaxKeyedFields = perObject,
                MaxTotalKeyedFields = cumulative
            })
            .Build());

    private static NewSchema TwoFields() => new() { Removed = new Node(), Kept = new Node() };

    [Fact]
    public void Serialize_KeyedObjectWithinBothLimits_Succeeds()
    {
        byte[] payload = With(perObject: 2, cumulative: 2).Serialize(TwoFields());

        Assert.NotEmpty(payload);
    }

    [Fact]
    public void Serialize_KeyedObjectOverThePerObjectLimit_NamesThatLimit()
    {
        var ex = Assert.Throws<BinaryLimitException>(
            () => With(perObject: 1, cumulative: 100).Serialize(TwoFields()));

        Assert.StartsWith("Keyed field count", ex.Message);
    }

    [Fact]
    public void Serialize_KeyedObjectsOverTheCumulativeLimit_NameThatLimit()
    {
        var ex = Assert.Throws<BinaryLimitException>(
            () => With(perObject: 100, cumulative: 1).Serialize(TwoFields()));

        Assert.Contains("Cumulative keyed field count", ex.Message);
    }

    [Fact]
    public void Serialize_KeyedObjectBreachingBothAtOnce_ReportsThePerObjectLimitFirst()
    {
        var ex = Assert.Throws<BinaryLimitException>(
            () => With(perObject: 1, cumulative: 1).Serialize(TwoFields()));

        Assert.StartsWith("Keyed field count", ex.Message);
    }

    [Fact]
    public void Deserialize_KeyedObjectOverThePerObjectLimit_NamesThatLimit()
    {
        byte[] payload = new BinarySerializer().Serialize(TwoFields());

        var ex = Assert.Throws<BinaryLimitException>(
            () => With(perObject: 1, cumulative: 100).Deserialize<NewSchema>(payload));

        Assert.StartsWith("Keyed field count", ex.Message);
    }

    [Fact]
    public void Deserialize_KeyedObjectOverTheCumulativeLimit_NamesThatLimit()
    {
        byte[] payload = new BinarySerializer().Serialize(TwoFields());

        var ex = Assert.Throws<BinaryLimitException>(
            () => With(perObject: 100, cumulative: 1).Deserialize<NewSchema>(payload));

        Assert.Contains("Cumulative keyed field count", ex.Message);
    }
}
