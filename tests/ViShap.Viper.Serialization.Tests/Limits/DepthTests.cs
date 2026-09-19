using System.Collections;
using System.Collections.Immutable;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-26…LIM-31: which values enter a depth scope, where the exact boundary lies, and that the
/// counter unwinds — on success, on failure, and across siblings. Depth is accounted by the engine,
/// so the same boundary holds on read and on write.
/// </summary>
public class DepthTests
{
    private static BinarySerializer Limited(int maxDepth) =>
        new(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxDepth = maxDepth })
            .Build());

    /// <summary>
    /// Asserts that <paramref name="twoLevels"/> — a container holding one structural value — needs
    /// two depth scopes, which is how a shape is shown to enter one of its own.
    /// </summary>
    private static void AssertTheInnerValueEntersADepthScope<T>(T twoLevels)
    {
        byte[] payload = new BinarySerializer().Serialize(twoLevels);

        Assert.Throws<BinaryLimitException>(() => Limited(1).Serialize(twoLevels));
        Assert.NotEmpty(Limited(2).Serialize(twoLevels));

        Assert.Throws<BinaryLimitException>(() => Limited(1).Deserialize<T>(payload));
        Assert.NotNull(Limited(2).Deserialize<T>(payload));
    }

    /// <summary>
    /// Asserts that <paramref name="oneLevel"/> — a container of scalars — needs a single scope, so
    /// none of its elements claimed one.
    /// </summary>
    private static void AssertTheElementsEnterNoDepthScope<T>(T oneLevel)
    {
        byte[] payload = new BinarySerializer().Serialize(oneLevel);

        Assert.NotEmpty(Limited(1).Serialize(oneLevel));
        Assert.NotNull(Limited(1).Deserialize<T>(payload));
    }

    // --- LIM-26: structural values enter a scope, scalars do not --------------------------------

    [Fact]
    public void MemberEncodedObject_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<Person> { new() { Name = "A", Age = 1 } });

    [Fact]
    public void Array_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<int[]> { new[] { 1, 2 } });

    [Fact]
    public void Collection_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<List<int>> { new() { 1, 2 } });

    [Fact]
    public void Dictionary_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<Dictionary<int, int>> { new() { [1] = 2 } });

    [Fact]
    public void Tuple_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<Tuple<int, int>> { new(1, 2) });

    [Fact]
    public void ValueTuple_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<(int, int)> { (1, 2) });

    [Fact]
    public void KeyValuePair_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<KeyValuePair<int, int>> { new(1, 2) });

    [Fact]
    public void MultiDimensionalArray_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<int[,]> { new[,] { { 1, 2 } } });

    [Fact]
    public void Lazy_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<Lazy<int>> { new(7) });

    [Fact]
    public void ImmutableArray_EntersADepthScope() =>
        AssertTheInnerValueEntersADepthScope(new List<ImmutableArray<int>> { ImmutableArray.Create(1, 2) });

    [Fact]
    public void Strings_EnterNoDepthScope() =>
        AssertTheElementsEnterNoDepthScope(new List<string> { "a", "b" });

    [Fact]
    public void Primitives_EnterNoDepthScope() =>
        AssertTheElementsEnterNoDepthScope(new List<int> { 1, 2, 3 });

    [Fact]
    public void BitArray_EntersNoDepthScope() =>
        // §22.4 encodes a BitArray as a scalar, so the CLR shape does not decide the accounting.
        AssertTheElementsEnterNoDepthScope(new List<BitArray> { new(8), new(16) });

    [Fact]
    public void Guids_EnterNoDepthScope() =>
        AssertTheElementsEnterNoDepthScope(new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

    // --- LIM-27: the exact boundary --------------------------------------------------------------

    [Fact]
    public void Deserialize_AtExactlyMaxDepth_Succeeds()
    {
        // Three nesting steps produce four collection levels.
        byte[] frame = Wire.NestedCollections(3);

        Assert.NotNull(Limited(4).Deserialize<Tree>(frame));
    }

    [Fact]
    public void Deserialize_OneLevelDeeperThanMaxDepth_ThrowsLimit()
    {
        byte[] frame = Wire.NestedCollections(3);

        Assert.Throws<BinaryLimitException>(() => Limited(3).Deserialize<Tree>(frame));
    }

    [Fact]
    public void Serialize_AtExactlyMaxDepth_Succeeds()
    {
        Assert.NotEmpty(Limited(4).Serialize(NestedTree(3)));
    }

    [Fact]
    public void Serialize_OneLevelDeeperThanMaxDepth_ThrowsLimit()
    {
        Assert.Throws<BinaryLimitException>(() => Limited(3).Serialize(NestedTree(3)));
    }

    // --- LIM-28…LIM-30: the counter unwinds (L2) ------------------------------------------------

    [Fact]
    public void EnterDepth_OnSuccessfulExit_RestoresThePreviousDepth()
    {
        var budget = new SerializationBudget(SerializationLimits.Default with { MaxDepth = 4 });

        using (var outer = budget.EnterDepth())
        {
            Assert.Equal(1, budget.Depth);

            using (var inner = budget.EnterDepth())
                Assert.Equal(2, budget.Depth);

            Assert.Equal(1, budget.Depth);
        }

        Assert.Equal(0, budget.Depth);
    }

    [Fact]
    public void EnterDepth_WhenTheScopeUnwindsOnAnException_RestoresThePreviousDepth()
    {
        var budget = new SerializationBudget(SerializationLimits.Default with { MaxDepth = 4 });

        Assert.Throws<InvalidOperationException>(() => Fail(budget));

        Assert.Equal(0, budget.Depth);

        static void Fail(SerializationBudget budget)
        {
            using var scope = budget.EnterDepth();
            throw new InvalidOperationException("unwinding");
        }
    }

    [Fact]
    public void EnterDepth_AtTheLimit_LeavesTheDepthUnchanged()
    {
        var budget = new SerializationBudget(SerializationLimits.Default with { MaxDepth = 1 });

        using var scope = budget.EnterDepth();
        Assert.Equal(1, budget.Depth);

        Assert.Throws<BinaryLimitException>(() => Enter(budget));

        Assert.Equal(1, budget.Depth);

        static void Enter(SerializationBudget budget)
        {
            using var scope = budget.EnterDepth();
        }
    }

    [Fact]
    public void DepthScope_DisposedTwice_DecrementsOnlyOnce()
    {
        var budget = new SerializationBudget(SerializationLimits.Default with { MaxDepth = 4 });

        var outer = budget.EnterDepth();
        var inner = budget.EnterDepth();
        inner.Dispose();
        inner.Dispose();

        Assert.Equal(1, budget.Depth);

        outer.Dispose();
        Assert.Equal(0, budget.Depth);
    }

    // --- LIM-31: siblings unwind independently ---------------------------------------------------

    [Fact]
    public void Serialize_SiblingBranches_DoNotAccumulateDepth()
    {
        // Five structural nodes, but no path through them is longer than three.
        var siblings = new List<List<List<int>>>
        {
            new() { new() { 1 } },
            new() { new() { 2 } }
        };

        Assert.NotEmpty(Limited(3).Serialize(siblings));
        Assert.Throws<BinaryLimitException>(() => Limited(2).Serialize(siblings));
    }

    [Fact]
    public void Deserialize_SiblingBranches_DoNotAccumulateDepth()
    {
        var siblings = new List<List<List<int>>>
        {
            new() { new() { 1 } },
            new() { new() { 2 } }
        };
        byte[] payload = new BinarySerializer().Serialize(siblings);

        Assert.Equal(2, Limited(3).Deserialize<List<List<List<int>>>>(payload)!.Count);
        Assert.Throws<BinaryLimitException>(
            () => Limited(2).Deserialize<List<List<List<int>>>>(payload));
    }

    private static Tree NestedTree(int steps)
    {
        var root = new Tree();
        var current = root;
        for (int step = 0; step < steps; step++)
        {
            var child = new Tree();
            current.Add(child);
            current = child;
        }

        return root;
    }
}
