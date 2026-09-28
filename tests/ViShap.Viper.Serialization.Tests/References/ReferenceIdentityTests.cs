using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.References;

/// <summary>
/// Pins REF-01, REF-02, REF-04 and CYC-01…CYC-03, CYC-09: with <c>PreserveReferences</c> identity
/// survives the round trip and a cycle becomes representable; without it a cycle is refused while
/// writing rather than followed.
/// </summary>
public class ReferenceIdentityTests
{
    private readonly BinarySerializer _serializer = new();

    private static BinarySerializer WithReferences() =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    private static T? RoundTrip<T>(BinarySerializer serializer, T value) =>
        serializer.Deserialize<T>(serializer.Serialize(value));

    [Fact]
    public void Deserialize_SharedCollection_PreservesIdentity()
    {
        var serializer = WithReferences();
        var shared = new List<int> { 1, 2, 3 };

        var result = RoundTrip(serializer, new SharedLists { A = shared, B = shared })!;

        Assert.Same(result.A, result.B);
        Assert.Equal(shared, result.A);
    }

    [Fact]
    public void Deserialize_SharedCollectionWithoutPreserveReferences_CreatesDistinctInstances()
    {
        var shared = new List<int> { 1, 2, 3 };

        var result = RoundTrip(_serializer, new SharedLists { A = shared, B = shared })!;

        Assert.NotSame(result.A, result.B);
        Assert.Equal(result.A, result.B);
    }

    [Fact]
    public void Deserialize_SharedObject_PreservesIdentity()
    {
        var serializer = WithReferences();
        var shared = new Node { Value = 9 };

        var result = RoundTrip(serializer, new List<Node> { shared, shared })!;

        Assert.Same(result[0], result[1]);
        Assert.Equal(9, result[0].Value);
    }

    [Fact]
    public void Deserialize_EqualButDistinctObjects_StayDistinct()
    {
        var serializer = WithReferences();
        var source = new List<Node> { new() { Value = 1 }, new() { Value = 1 } };

        var result = RoundTrip(serializer, source)!;

        Assert.NotSame(result[0], result[1]);
    }

    [Fact]
    public void Deserialize_SharedDagWithoutACycle_RoundTrips()
    {
        var serializer = WithReferences();
        var leaf = new Cyclic { Name = "leaf" };
        var source = new List<Cyclic>
        {
            new() { Name = "left", Next = leaf },
            new() { Name = "right", Next = leaf }
        };

        var result = RoundTrip(serializer, source)!;

        Assert.Same(result[0].Next, result[1].Next);
        Assert.Equal("leaf", result[0].Next!.Name);
    }

    [Fact]
    public void Deserialize_SelfReferencingList_RoundTrips()
    {
        var serializer = WithReferences();
        var source = new List<object>();
        source.Add(source);

        var result = RoundTrip(serializer, source)!;

        Assert.Single(result);
        Assert.Same(result, result[0]);
    }

    [Fact]
    public void Deserialize_CycleWithPreserveReferences_RoundTrips()
    {
        var serializer = WithReferences();
        var first = new Cyclic { Name = "a" };
        first.Next = first;

        var result = RoundTrip(serializer, first)!;

        Assert.Same(result, result.Next);
    }

    [Fact]
    public void Deserialize_TwoObjectCycle_RoundTrips()
    {
        var serializer = WithReferences();
        var first = new Cyclic { Name = "a" };
        var second = new Cyclic { Name = "b", Next = first };
        first.Next = second;

        var result = RoundTrip(serializer, first)!;

        Assert.Equal("b", result.Next!.Name);
        Assert.Same(result, result.Next.Next);
    }

    [Fact]
    public void Serialize_CycleWithoutPreserveReferences_ThrowsType()
    {
        var first = new Cyclic { Name = "a" };
        first.Next = first;

        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(first));
    }

    [Fact]
    public void Serialize_CycleThroughACollectionWithoutPreserveReferences_ThrowsType()
    {
        var source = new List<object>();
        source.Add(source);

        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(source));
    }
}
