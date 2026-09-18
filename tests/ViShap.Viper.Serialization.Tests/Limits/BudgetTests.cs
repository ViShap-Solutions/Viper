using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-21, LIM-24, HST-26 and V0-07: the cumulative budgets bound a whole operation, they are
/// fresh for the next one, and a sequence with no known count is abandoned at its limit rather than
/// enumerated.
/// </summary>
public class BudgetTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    [Fact]
    public void Deserialize_UnknownKeyedFieldsOverCumulativeBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxTotalKeyedFields = 2 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<EmptyContract>(Wire.KeyedFields(3)));
    }

    [Fact]
    public void Deserialize_UnknownKeyedFieldsWithinBudget_IsAccepted()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxTotalKeyedFields = 3 });

        Assert.NotNull(serializer.Deserialize<EmptyContract>(Wire.KeyedFields(3)));
    }

    [Fact]
    public void Deserialize_UnknownKeyedFields_DoNotConsumeTheElementBudget()
    {
        // Field counts are schema metadata; only data spends MaxTotalElements.
        var serializer = Limited(SerializationLimits.Default with { MaxTotalElements = 1 });

        Assert.NotNull(serializer.Deserialize<EmptyContract>(Wire.KeyedFields(3)));
    }

    [Fact]
    public void Deserialize_TwoIndividuallyLegalCollections_ShareTheElementBudget()
    {
        byte[] payload = new BinarySerializer().Serialize(
            new List<List<int>> { new() { 1, 2, 3 }, new() { 4, 5, 6 } });

        var serializer = Limited(SerializationLimits.Default with
        {
            MaxCollectionLength = 4,
            MaxTotalElements = 5
        });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<List<List<int>>>(payload));
    }

    [Fact]
    public void Deserialize_AfterAFailedOperation_StartsFromAFreshBudget()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxTotalElements = 8 });
        byte[] payload = new BinarySerializer().Serialize(new List<int> { 1, 2, 3 });

        Assert.Equal(3, serializer.Deserialize<List<int>>(payload)!.Count);
        Assert.Equal(3, serializer.Deserialize<List<int>>(payload)!.Count);
        Assert.Equal(3, serializer.Deserialize<List<int>>(payload)!.Count);
    }

    [Fact]
    public void Deserialize_V0PayloadOverMaxPayloadBytes_ThrowsLimit()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .AllowV0Fallback()
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 1 })
                .Build());

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<int>(BitConverter.GetBytes(123)));
    }

    [Fact]
    public void Serialize_LazySequenceOverCollectionLimit_StopsEnumeratingAtTheLimit()
    {
        int visited = 0;

        IEnumerable<int> Items()
        {
            while (true)
            {
                visited++;
                yield return visited;
            }
        }

        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 8 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize<IEnumerable<int>>(Items()));
        Assert.True(visited <= 9, $"enumerated {visited} items for a limit of 8");
    }
}
