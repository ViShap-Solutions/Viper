using System.Collections.Immutable;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.References;

/// <summary>
/// Pins REF-09…REF-11 and CYC-04…CYC-06: when an object becomes visible to the references inside it.
/// A container that exists before its children closes a cycle through itself; one that cannot exist
/// until its children are known is registered on completion, so a reference reaching it while it is
/// still being built fails deterministically instead of yielding half an object.
/// </summary>
public class RegistrationOrderTests
{
    private readonly BinarySerializer _plain = new();

    private readonly BinarySerializer _framed =
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    private T? RoundTrip<T>(T value) => _framed.Deserialize<T>(_framed.Serialize(value));

    [Fact]
    public void Deserialize_ReferenceIntoAMutableCollectionFromItsOwnElement_Closes()
    {
        // The element can only point back at the list if the list was registered before the element
        // was read.
        var list = new List<Holder>();
        list.Add(new Holder { Name = "a", Items = list });

        var result = RoundTrip(list)!;

        Assert.Same(result, result[0].Items);
        Assert.Equal("a", result[0].Name);
    }

    [Fact]
    public void Deserialize_CycleThroughADictionary_RoundTrips()
    {
        var root = new DictionaryHolder { Name = "root" };
        root.Entries = new Dictionary<string, DictionaryHolder> { ["self"] = root };

        var result = RoundTrip(root)!;

        Assert.Equal("root", result.Name);
        Assert.Same(result, result.Entries!["self"]);
    }

    [Fact]
    public void Deserialize_CycleThroughAPolymorphicMember_RoundTrips()
    {
        var derived = new CyclicDerived { A = 1 };
        derived.Next = derived;

        var result = _framed.Deserialize<CyclicBase>(_framed.Serialize<CyclicBase>(derived))!;

        Assert.Equal(1, Assert.IsType<CyclicDerived>(result).A);
        Assert.Same(result, result.Next);
    }

    [Fact]
    public void Deserialize_CycleThroughAStructMember_RoundTrips()
    {
        var root = new BoxedCycle { Name = "root" };
        root.Box = new HolderBox { Inner = root };

        var result = RoundTrip(root)!;

        Assert.Equal("root", result.Name);
        Assert.Same(result, result.Box.Inner);
    }

    [Fact]
    public void Serialize_CycleThroughAStructMemberWithoutPreserveReferences_ThrowsType()
    {
        // The struct is not framed, but the object it carries is still an ancestor of itself.
        var root = new BoxedCycle { Name = "root" };
        root.Box = new HolderBox { Inner = root };

        Assert.Throws<BinaryTypeException>(() => _plain.Serialize(root));
    }

    [Fact]
    public void Deserialize_SharedArray_ResolvesToOneInstance()
    {
        // Registered on completion, which is late enough for the second reference and too late for
        // one reaching into it while it is still being filled.
        int[] array = [1, 2, 3];

        var result = RoundTrip(new SharedArrays { A = array, B = array })!;

        Assert.Same(result.A, result.B);
        Assert.Equal([1, 2, 3], result.A!);
    }

    [Fact]
    public void Deserialize_SharedImmutableCollection_ResolvesToOneInstance()
    {
        var list = ImmutableList.Create(1, 2, 3);

        var result = RoundTrip(new SharedImmutable { A = list, B = list })!;

        Assert.Same(result.A, result.B);
        Assert.Equal([1, 2, 3], result.A!);
    }

    [Fact]
    public void Deserialize_SharedTuple_ResolvesToOneInstance()
    {
        var tuple = Tuple.Create(1, "one");

        var result = RoundTrip(new SharedTuples { A = tuple, B = tuple })!;

        Assert.Same(result.A, result.B);
        Assert.Equal(1, result.A!.Item1);
    }

    [Fact]
    public void Deserialize_ReferenceIntoAnArrayStillBeingBuilt_ThrowsFormat()
    {
        var array = new ArrayHolder[1];
        array[0] = new ArrayHolder { Items = array };

        byte[] payload = _framed.Serialize(array);

        AssertEx.Throws<BinaryFormatException>(
            "still being constructed", () => _framed.Deserialize<ArrayHolder[]>(payload));
    }
}
