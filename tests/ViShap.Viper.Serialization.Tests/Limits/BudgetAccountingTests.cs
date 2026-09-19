using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-16, LIM-17, LIM-25, LIM-40 and LIM-41 at the layer that owns them: a count is checked
/// and charged in one indivisible step, the cumulative counters only ever grow, and reading a
/// payload whose header declares another reference mode keeps accounting on the same budget.
/// </summary>
public class BudgetAccountingTests
{
    private static SerializationOperation Operation(SerializationLimits limits) =>
        new(limits, keys: null, preserveReferences: false, requireEncryption: false,
            requireChecksum: false);

    // --- LIM-16: one validated count, one charge -------------------------------------------------

    [Fact]
    public void Validate_ChargesTheElementBudgetExactlyOnce()
    {
        var operation = Operation(SerializationLimits.Default);

        ElementCount.Validate(5, CountKind.Collection, operation, "Collection count");

        Assert.Equal(5, operation.Budget.TotalElements);
    }

    [Fact]
    public void Validate_ChargesEachCountSeparately()
    {
        var operation = Operation(SerializationLimits.Default);

        ElementCount.Validate(5, CountKind.Collection, operation, "Collection count");
        ElementCount.Validate(3, CountKind.Array, operation, "Array length");

        Assert.Equal(8, operation.Budget.TotalElements);
    }

    [Fact]
    public void Validate_OverTheLimit_ChargesNothing()
    {
        // Checking and charging are one step, so a refused count leaves the budget untouched.
        var operation = Operation(SerializationLimits.Default with { MaxCollectionLength = 4 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(5, CountKind.Collection, operation, "Collection count"));

        Assert.Equal(0, operation.Budget.TotalElements);
    }

    [Fact]
    public void ValidateShape_ChargesTheProductOnce()
    {
        var operation = Operation(SerializationLimits.Default);

        ElementCount.ValidateShape([2, 3], operation, "Multi-dimensional array");

        Assert.Equal(6, operation.Budget.TotalElements);
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
    public void WithPreserveReferences_KeepsTheSameBudgetAndPhasePolicy()
    {
        var operation = Operation(SerializationLimits.Default);
        operation.Budget.ConsumeElements(7);

        var following = operation.WithPreserveReferences(true);

        Assert.Same(operation.Budget, following.Budget);
        Assert.Equal(7, following.Budget.TotalElements);
        Assert.True(following.PreserveReferences);
    }

    [Fact]
    public void WithPreserveReferences_ForTheSameMode_ReturnsTheSameOperation()
    {
        var operation = Operation(SerializationLimits.Default);

        Assert.Same(operation, operation.WithPreserveReferences(false));
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
            () => ElementCount.Validate(3, CountKind.Array, operation, "Array length"));

        Assert.Equal(3, ElementCount.Validate(3, CountKind.Collection, operation, "Collection count").Value);
    }

    [Fact]
    public void Validate_CollectionCount_IsBoundedByMaxCollectionLength()
    {
        var operation = Operation(SerializationLimits.Default with { MaxCollectionLength = 2 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(3, CountKind.Collection, operation, "Collection count"));

        Assert.Equal(3, ElementCount.Validate(3, CountKind.Array, operation, "Array length").Value);
    }

    [Fact]
    public void Validate_DictionaryCount_IsBoundedByMaxDictionaryEntries()
    {
        var operation = Operation(SerializationLimits.Default with { MaxDictionaryEntries = 2 });

        Assert.Throws<BinaryLimitException>(
            () => ElementCount.Validate(3, CountKind.Dictionary, operation, "Dictionary entry count"));

        Assert.Equal(3, ElementCount.Validate(3, CountKind.Collection, operation, "Collection count").Value);
    }

    // --- LIM-42: a declared count does not size the first allocation ------------------------------

    [Fact]
    public void CapacityHint_ForALargeCount_IsBoundedByTheGrowthHint()
    {
        var operation = Operation(SerializationLimits.Default);

        var count = ElementCount.Validate(1_000_000, CountKind.Collection, operation, "Collection count");

        Assert.Equal(1_024, count.CapacityHint);
    }

    [Fact]
    public void CapacityHint_ForASmallCount_IsTheCountItself()
    {
        var operation = Operation(SerializationLimits.Default);

        var count = ElementCount.Validate(7, CountKind.Collection, operation, "Collection count");

        Assert.Equal(7, count.CapacityHint);
    }
}
