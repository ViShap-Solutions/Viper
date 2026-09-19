using System.Collections.Immutable;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Plan §19.4: RT-53…RT-57 and RT-60, the composite family of contract §23. A composite has a fixed,
/// type-determined child layout (§22.5), so what is asserted is that every slot comes back in its
/// own place — including the eighth one, where a long tuple hides its tail.
/// </summary>
public abstract partial class Corpus
{
    [Fact]
    public void Deserialize_KeyValuePair_RestoresTheKeyAndTheValue()
    {
        var restored = RoundTrip(new KeyValuePair<string, int>("a", 1));

        Assert.Equal("a", restored.Key);
        Assert.Equal(1, restored.Value);

        var withNulls = RoundTrip(new KeyValuePair<string?, Person?>(null, null));
        Assert.Null(withNulls.Key);
        Assert.Null(withNulls.Value);
    }

    [Fact]
    public void Deserialize_Tuple_RestoresEveryItemInDeclarationOrder()
    {
        Assert.Equal(Tuple.Create(1), RoundTrip(Tuple.Create(1)));
        Assert.Equal(Tuple.Create(1, "a"), RoundTrip(Tuple.Create(1, "a")));

        var seven = Tuple.Create(1, 2, 3, 4, 5, 6, 7);
        var restored = RoundTrip(seven)!;

        Assert.Equal(seven, restored);
        Assert.Equal(1, restored.Item1);
        Assert.Equal(7, restored.Item7);
    }

    [Fact]
    public void Deserialize_ValueTuple_RestoresEveryItemInDeclarationOrder()
    {
        Assert.Equal(ValueTuple.Create(1), RoundTrip(ValueTuple.Create(1)));
        Assert.Equal((1, "a"), RoundTrip((1, "a")));

        var seven = (1, 2, 3, 4, 5, 6, 7);
        var restored = RoundTrip(seven);

        Assert.Equal(seven, restored);
        Assert.Equal(1, restored.Item1);
        Assert.Equal(7, restored.Item7);
    }

    [Fact]
    public void Deserialize_LongValueTuple_RestoresTheItemsHeldInTheRest()
    {
        // Ten items are a ValueTuple of seven plus a nested one, so the eighth slot is itself a
        // composite and the corpus must follow it rather than stop at Item7.
        var long_ = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10);

        var restored = RoundTrip(long_);

        Assert.Equal(long_, restored);
        Assert.Equal(8, restored.Item8);
        Assert.Equal(10, restored.Item10);
    }

    [Fact]
    public void Deserialize_LongTuple_RestoresTheItemsHeldInTheRest()
    {
        var long_ = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
            1, 2, 3, 4, 5, 6, 7, new Tuple<int, int>(8, 9));

        var restored = RoundTrip(long_)!;

        Assert.Equal(long_, restored);
        Assert.Equal(8, restored.Rest.Item1);
        Assert.Equal(9, restored.Rest.Item2);
    }

    [Fact]
    public void Deserialize_NestedTuple_RestoresEachLevel()
    {
        var nested = ((1, 2), (3, "a"), Tuple.Create(4));

        var restored = RoundTrip(nested);

        Assert.Equal(2, restored.Item1.Item2);
        Assert.Equal("a", restored.Item2.Item2);
        Assert.Equal(4, restored.Item3.Item1);
    }

    [Fact]
    public void Deserialize_TupleWithNullableElements_RestoresNullPerSlot()
    {
        var restored = RoundTrip((7, (int?)null, (string?)null, (int?)3));

        Assert.Equal(7, restored.Item1);
        Assert.Null(restored.Item2);
        Assert.Null(restored.Item3);
        Assert.Equal(3, restored.Item4);
    }

    [Fact]
    public void Deserialize_Lazy_RestoresTheMaterializedValue() =>
        // The materialization semantics themselves are pinned in LazyTests; the corpus only has to
        // show that the shape survives every profile.
        Assert.Equal(7, RoundTrip(new Lazy<int>(() => 7))!.Value);

    [Fact]
    public void Deserialize_ImmutableArray_RestoresThePopulatedEmptyAndDefaultStates()
    {
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableArray.Create(1, 2, 3)).ToArray());

        var empty = RoundTrip(ImmutableArray<int>.Empty);
        Assert.False(empty.IsDefault);
        Assert.True(empty.IsEmpty);

        Assert.True(RoundTrip(default(ImmutableArray<int>)).IsDefault);
    }

    [Fact]
    public void Deserialize_MemberEncodedObject_RestoresEveryMemberAtEveryLevel()
    {
        // RT-C10: the last row of §23 — a type no formatter claims, encoded member by member. The
        // member plan itself belongs to §15; what the corpus asserts is that the shape survives
        // every profile, nested and with a null member.
        var restored = RoundTrip(new Cyclic
        {
            Name = "root",
            Next = new Cyclic { Name = "leaf", Next = null }
        })!;

        Assert.Equal("root", restored.Name);
        Assert.Equal("leaf", restored.Next!.Name);
        Assert.Null(restored.Next.Next);

        var person = RoundTrip(new Person { Name = "Ada", Age = 36 })!;
        Assert.Equal("Ada", person.Name);
        Assert.Equal(36, person.Age);
    }

    [Fact]
    public void Deserialize_ImmutableArrayOfReferences_RestoresNullElements()
    {
        var restored = RoundTrip(ImmutableArray.Create<string?>("a", null, "c"));

        Assert.Equal(3, restored.Length);
        Assert.Null(restored[1]);
    }
}
