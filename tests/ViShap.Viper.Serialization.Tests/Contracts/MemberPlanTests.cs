using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-01…CTR-09 and CTR-19: which members the positional plan carries, in which order, and
/// when a self-contradicting declaration is refused. The order assertions read the bare V0 payload,
/// because under both wire formats the member layout is the same bytes.
/// </summary>
public class MemberPlanTests
{
    private readonly BinarySerializer _serializer = new();

    private readonly BinarySerializer _compact =
        new(BinarySerializerOptions.Configure().WithVersion(0).Build());

    [Fact]
    public void Deserialize_PublicReadWritePropertyAndField_AreCarried()
    {
        var source = new MemberSelection { Property = 7, Field = 9 };

        var result = _serializer.Deserialize<MemberSelection>(_serializer.Serialize(source))!;

        Assert.Equal(7, result.Property);
        Assert.Equal(9, result.Field);
    }

    [Fact]
    public void Serialize_MemberSelection_CarriesTheTwoEligibleMembersAndNothingElse()
    {
        // Field, then Property: the two eligible members in ordinal name order, and no room for the
        // get-only property, the computed one, the readonly field, the indexer, the private field or
        // an auto-property backing field.
        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(9);
            writer.Write(7);
        });

        Assert.Equal(expected, _compact.Serialize(new MemberSelection { Property = 7, Field = 9 }));
    }

    [Fact]
    public void Deserialize_GetOnlyAndReadOnlyMembers_KeepTheirDeclaredValues()
    {
        var result = _serializer.Deserialize<MemberSelection>(
            _serializer.Serialize(new MemberSelection { Property = 7, Field = 9 }))!;

        Assert.Equal(111, result.GetOnly);
        Assert.Equal(222, result.Computed);
        Assert.Equal(333, result.ReadOnlyField);
        Assert.Equal(444, result.Hidden);
    }

    [Fact]
    public void Deserialize_IgnoredMembers_AreNotCarried()
    {
        var source = new IgnoredMembers { Kept = 5, DroppedProperty = 6, DroppedField = 7 };

        var result = _serializer.Deserialize<IgnoredMembers>(_serializer.Serialize(source))!;

        Assert.Equal(5, result.Kept);
        Assert.Equal(0, result.DroppedProperty);
        Assert.Equal(0, result.DroppedField);
    }

    [Fact]
    public void Serialize_IgnoredMembers_CarriesOnlyTheKeptOne()
    {
        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(5);
        });

        Assert.Equal(
            expected,
            _compact.Serialize(new IgnoredMembers { Kept = 5, DroppedProperty = 6, DroppedField = 7 }));
    }

    [Fact]
    public void Deserialize_IncludedNonPublicMembers_AreCarried()
    {
        var source = new IncludedNonPublicMembers { FieldValue = 11, PropertyValue = "kept" };

        var result = _serializer.Deserialize<IncludedNonPublicMembers>(
            _serializer.Serialize(source))!;

        Assert.Equal(11, result.FieldValue);
        Assert.Equal("kept", result.PropertyValue);
    }

    [Fact]
    public void Serialize_ExplicitOrder_WritesOrderedMembersFirstThenTheRestByName()
    {
        var source = new ExplicitOrder { First = 1, Second = 2, Another = 3, Unordered = 4 };

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(1);        // [BinaryOrder(1)]
            writer.Write(2);        // [BinaryOrder(2)]
            writer.Write(3);        // "Another"
            writer.Write(4);        // "Unordered"
        });

        Assert.Equal(expected, _compact.Serialize(source));
    }

    [Fact]
    public void Serialize_WithoutExplicitOrder_WritesMembersInOrdinalNameOrder()
    {
        // Ordinal order puts "Bravo" and "Charlie" before "alpha"; a culture-aware comparer would
        // put "alpha" first, so the payload distinguishes the two.
        var source = new OrdinalOrder { Bravo = 10, Charlie = 20, alpha = 30 };

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(10);
            writer.Write(20);
            writer.Write(30);
        });

        Assert.Equal(expected, _compact.Serialize(source));
    }

    [Fact]
    public void Serialize_WithoutExplicitOrder_ProducesTheSameLayoutEveryTime()
    {
        var source = new OrdinalOrder { Bravo = 10, Charlie = 20, alpha = 30 };

        Assert.Equal(_compact.Serialize(source), _compact.Serialize(source));
    }

    [Fact]
    public void Serialize_DuplicateExplicitOrder_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "duplicate [BinaryOrder]", () => _serializer.Serialize(new DuplicateOrder()));
    }

    [Fact]
    public void Serialize_KeyWithoutContract_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "[BinaryKey]", () => _serializer.Serialize(new StrayKey { Value = 1 }));
    }

    [Fact]
    public void Serialize_ContradictionOnALaterMember_IsRefusedBeforeTheEarlierOneIsWritten()
    {
        // The plan is built for the whole type before the first field is written, so a contradiction
        // anywhere in it stops the write before any member reaches the destination.
        using var destination = new MemoryStream();

        Assert.Throws<BinaryTypeException>(
            () => _compact.Serialize(destination, new LateContradiction { Early = "written-first" }));

        AssertEx.DoesNotContainBytes(destination.ToArray(), "written-first"u8);
    }

    [Fact]
    public void Deserialize_StructWithReferenceMembers_RoundTrips()
    {
        var source = new StructWithReferences
        {
            Name = "boxed",
            Values = [1, 2, 3],
            Node = new Node { Value = 42 }
        };

        var result = _serializer.Deserialize<StructWithReferences>(_serializer.Serialize(source));

        Assert.Equal("boxed", result.Name);
        Assert.Equal([1, 2, 3], result.Values);
        Assert.Equal(42, result.Node!.Value);
    }

    [Fact]
    public void Deserialize_GraphCrossingFourFormatterFamilies_RoundTrips()
    {
        var featured = new Entry
        {
            Title = "featured",
            Stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Scores = [1, 2]
        };

        var source = new Catalogue
        {
            Sections = new Dictionary<string, List<Entry>>
            {
                ["a"] = [featured, new Entry { Title = "second", Scores = [] }]
            },
            Ranked = [(1, "gold"), (2, "silver")],
            Featured = featured
        };

        var result = _serializer.Deserialize<Catalogue>(_serializer.Serialize(source))!;

        Assert.Equal("featured", result.Sections!["a"][0].Title);
        Assert.Equal(featured.Stamp, result.Sections["a"][0].Stamp);
        Assert.Equal([1, 2], result.Sections["a"][0].Scores!);
        Assert.Equal("second", result.Sections["a"][1].Title);
        Assert.Empty(result.Sections["a"][1].Scores!);
        Assert.Equal([(1, "gold"), (2, "silver")], result.Ranked!);
        Assert.Equal("featured", result.Featured!.Title);
    }
}
