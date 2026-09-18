using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-18, LIM-32, LIM-33 and CYC-10: nesting is bounded by a limit rather than by the stack,
/// and a container instance counts as a graph node exactly as a member-encoded object does.
/// </summary>
public class DepthAndNodeTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    private static Tree NestedTree(int depth)
    {
        var root = new Tree();
        var current = root;
        for (int i = 0; i < depth; i++)
        {
            var child = new Tree();
            current.Add(child);
            current = child;
        }

        return root;
    }

    [Fact]
    public void Serialize_RecursiveCollectionDeeperThanMaxDepth_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDepth = 4 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(NestedTree(30)));
    }

    [Fact]
    public void Serialize_RecursiveCollectionWithinMaxDepth_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDepth = 8 });

        Assert.NotEmpty(serializer.Serialize(NestedTree(3)));
    }

    [Fact]
    public void Deserialize_RecursiveCollectionDeeperThanMaxDepth_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDepth = 4 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<Tree>(Wire.NestedCollections(30)));
    }

    [Fact]
    public void Deserialize_DeeplyNestedCollectionPayload_FailsWithoutStackOverflow()
    {
        // A payload nested far past any stack depth must fail as an ordinary limit violation,
        // leaving the process alive.
        byte[] hostile = Wire.NestedCollections(200_000);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<Tree>(hostile));
    }

    [Fact]
    public void Deserialize_ContainersExceedingNodeBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxObjectGraphNodes = 1 });
        byte[] payload = new BinarySerializer().Serialize(new List<List<int>> { new(), new(), new() });

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<List<List<int>>>(payload));
    }

    [Fact]
    public void Deserialize_ContainersWithinNodeBudget_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxObjectGraphNodes = 4 });
        byte[] payload = new BinarySerializer().Serialize(new List<List<int>> { new(), new(), new() });

        Assert.Equal(3, serializer.Deserialize<List<List<int>>>(payload)!.Count);
    }

    [Fact]
    public void Deserialize_RepeatedReference_DoesNotConsumeASecondNode()
    {
        var withReferences = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());
        var shared = new Node { Value = 1 };
        byte[] payload = withReferences.Serialize(new List<Node> { shared, shared, shared });

        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .PreserveReferences()
                .WithLimits(SerializationLimits.Default with { MaxObjectGraphNodes = 2 })
                .Build());

        // One list plus one node: the two back references establish no further identity.
        Assert.Equal(3, reader.Deserialize<List<Node>>(payload)!.Count);
    }
}
