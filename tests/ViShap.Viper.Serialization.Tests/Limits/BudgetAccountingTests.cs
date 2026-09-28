using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-16, LIM-17, LIM-25, LIM-40 and LIM-41 at the layer that owns them: a count is checked
/// and charged in one indivisible step, the cumulative counters only ever grow, and reading a
/// payload whose header declares another reference mode keeps accounting on the same budget.
/// </summary>
public class BudgetAccountingTests
{
    private static OperationBox Operation(SerializationLimits limits) => new(limits);

    // --- LIM-16: one validated count, one charge -------------------------------------------------

    [Fact]
    public void Validate_ChargesTheElementBudgetExactlyOnce()
    {
        var operation = Operation(SerializationLimits.Default);

        ElementCount.Validate(5, CountKind.Collection, ref operation.State, "Collection count");

        Assert.Equal(5, operation.State.Budget.TotalElements);
    }

    [Fact]
    public void Validate_ChargesEachCountSeparately()
    {
        var operation = Operation(SerializationLimits.Default);

        ElementCount.Validate(5, CountKind.Collection, ref operation.State, "Collection count");
        ElementCount.Validate(3, CountKind.Array, ref operation.State, "Array length");

        Assert.Equal(8, operation.State.Budget.TotalElements);
    }

    [Fact]
    public void Validate_OverTheLimit_ChargesNothing()
    {
        // Checking and charging are one step, so a refused count leaves the budget untouched.
        var operation = Operation(SerializationLimits.Default with { MaxCollectionLength = 4 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(5, CountKind.Collection, ref operation.State, "Collection count"));

        Assert.Equal(0, operation.State.Budget.TotalElements);
    }

    [Fact]
    public void ValidateShape_ChargesTheProductOnce()
    {
        var operation = Operation(SerializationLimits.Default);

        ElementCount.ValidateShape([2, 3], ref operation.State, "Multi-dimensional array");

        Assert.Equal(6, operation.State.Budget.TotalElements);
    }

    [Fact]
    public void Deserialize_ACollectionAtTheCumulativeCeiling_IsChargedOnlyItsOwnElements()
    {
        // Three elements under a budget of three: a second charge for the same count would fail.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxTotalElements = 3 })
                .Build());
        byte[] payload = new BinarySerializer().Serialize(new List<int> { 1, 2, 3 });

        Assert.Equal(3, serializer.Deserialize<List<int>>(payload)!.Count);
    }

    // --- LIM-17: the cumulative counters never decrease -------------------------------------------

    [Fact]
    public void ConsumeElements_AcrossSeveralCharges_OnlyEverGrows()
    {
        var budget = new SerializationBudget(SerializationLimits.Default);
        long previous = 0;

        foreach (int charge in new[] { 1, 0, 7, 0, 13 })
        {
            budget.ConsumeElements(charge);

            Assert.True(budget.TotalElements >= previous);
            previous = budget.TotalElements;
        }

        Assert.Equal(21, budget.TotalElements);
    }

    [Fact]
    public void ConsumeElements_AfterARefusedCharge_KeepsTheEarlierTotal()
    {
        var budget = new SerializationBudget(SerializationLimits.Default with { MaxTotalElements = 10 });
        budget.ConsumeElements(6);

        Assert.Throws<BinaryLimitException>(() => budget.ConsumeElements(5));

        Assert.Equal(6, budget.TotalElements);
    }

    [Fact]
    public void ConsumeElements_WithANegativeCount_ThrowsFormatAndChargesNothing()
    {
        var budget = new SerializationBudget(SerializationLimits.Default);
        budget.ConsumeElements(4);

        var ex = Assert.Throws<BinaryFormatException>(() => budget.ConsumeElements(-1));

        Assert.IsNotType<BinaryLimitException>(ex);
        Assert.Equal(4, budget.TotalElements);
    }

    [Fact]
    public void ConsumeObjectGraphNodes_AndConsumeKeyedFields_FollowTheSamePattern()
    {
        var budget = new SerializationBudget(SerializationLimits.Default with
        {
            MaxObjectGraphNodes = 4,
            MaxTotalKeyedFields = 4
        });

        budget.ConsumeObjectGraphNodes(3);
        budget.ConsumeKeyedFields(3);

        Assert.Throws<BinaryLimitException>(() => budget.ConsumeObjectGraphNodes(2));
        Assert.Throws<BinaryLimitException>(() => budget.ConsumeKeyedFields(2));

        Assert.Equal(3, budget.ObjectGraphNodes);
        Assert.Equal(3, budget.KeyedFields);
    }

    // --- LIM-25: a reference-mode switch keeps the budget ----------------------------------------

    [Fact]
    public void ReadRoot_WithReferenceFraming_ChargesTheSameBudget()
    {
        // The payload's reference mode is a property of its traversal, not a second operation: the
        // budget charged before the payload is the one the payload keeps charging.
        byte[] payload = PayloadOf(new List<int> { 1, 2, 3, 4 }, preserveReferences: true);
        var operation = Operation(SerializationLimits.Default);
        operation.State.Budget.ConsumeElements(7);

        var reader = new WireReader(payload, ref operation.State);
        var list = Graph.ReadRoot<List<int>>(ref reader, target: null, preserveReferences: true);

        Assert.Equal([1, 2, 3, 4], list);
        Assert.Equal(11, operation.State.Budget.TotalElements);
        Assert.Equal(1, operation.State.Budget.ObjectGraphNodes);
    }

    [Fact]
    public void ReadRoot_WithReferenceFraming_ClosesItsTraversalWhenItEnds()
    {
        byte[] payload = PayloadOf(new List<int> { 1 }, preserveReferences: true);
        var operation = Operation(SerializationLimits.Default);

        var reader = new WireReader(payload, ref operation.State);
        Graph.ReadRoot<List<int>>(ref reader, target: null, preserveReferences: true);

        Assert.Null(operation.State.Graph.Read);
        Assert.Null(operation.State.Graph.Written);
        Assert.False(operation.State.PreserveReferences);
    }

    /// <summary>The payload of <paramref name="value"/> alone, as the engine writes it.</summary>
    private static byte[] PayloadOf<T>(T value, bool preserveReferences)
    {
        var operation = Operation(SerializationLimits.Default);
        using var buffer = new PayloadBuffer(1024, "payload");
        var writer = new WireWriter(buffer, ref operation.State);
        Graph.WriteRoot(ref writer, value, preserveReferences);
        writer.Flush();

        byte[] bytes = new byte[buffer.Length];
        buffer.CopyTo(bytes);
        return bytes;
    }

    [Fact]
    public void Deserialize_APayloadDeclaringReferences_SharesOneBudgetWithTheEnvelope()
    {
        // The header turns reference framing on for a serializer configured without it; the element
        // budget still bounds the whole operation.
        byte[] payload = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build())
            .Serialize(new List<int> { 1, 2, 3, 4 });

        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxTotalElements = 3 })
                .Build());

        Assert.Throws<BinaryLimitException>(() => reader.Deserialize<List<int>>(payload));
    }

    // --- LIM-41: the count kind selects the limit ------------------------------------------------

    [Fact]
    public void Validate_ArrayCount_IsBoundedByMaxArrayLength()
    {
        var operation = Operation(SerializationLimits.Default with { MaxArrayLength = 2 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(3, CountKind.Array, ref operation.State, "Array length"));

        Assert.Equal(3, ElementCount.Validate(3, CountKind.Collection, ref operation.State, "Collection count").Value);
    }

    [Fact]
    public void Validate_CollectionCount_IsBoundedByMaxCollectionLength()
    {
        var operation = Operation(SerializationLimits.Default with { MaxCollectionLength = 2 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(3, CountKind.Collection, ref operation.State, "Collection count"));

        Assert.Equal(3, ElementCount.Validate(3, CountKind.Array, ref operation.State, "Array length").Value);
    }

    [Fact]
    public void Validate_DictionaryCount_IsBoundedByMaxDictionaryEntries()
    {
        var operation = Operation(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(3, CountKind.Dictionary, ref operation.State, "Dictionary entry count"));

        Assert.Equal(3, ElementCount.Validate(3, CountKind.Collection, ref operation.State, "Collection count").Value);
    }

    // --- LIM-42: a declared count does not size the first allocation ------------------------------

    [Fact]
    public void CapacityHint_ForALargeCount_IsBoundedByTheGrowthHint()
    {
        var operation = Operation(SerializationLimits.Default);

        var count = ElementCount.Validate(1_000_000, CountKind.Collection, ref operation.State, "Collection count");

        Assert.Equal(1_024, count.CapacityHint);
    }

    [Fact]
    public void CapacityHint_ForASmallCount_IsTheCountItself()
    {
        var operation = Operation(SerializationLimits.Default);

        var count = ElementCount.Validate(7, CountKind.Collection, ref operation.State, "Collection count");

        Assert.Equal(7, count.CapacityHint);
    }
}
