using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins KEY-04…KEY-16: what a reader whose schema has moved on does with a field it does not know,
/// and what the declared length of a field is worth. A known field is decoded through a window it
/// cannot read past; an unknown one is stepped over in bounded chunks without a formatter ever being
/// asked for the type that vanished with it.
/// </summary>
public class KeyedEvolutionTests
{
    private readonly BinarySerializer _serializer = new();

    private static BinarySerializer Compact() =>
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    private static byte[] KeyedFrame(params Wire.KeyedField[] fields) =>
        Wire.Frame([.. Wire.NotNull, .. Wire.KeyedBody(fields)]);

    private static byte[] Int32Field(int value) => Wire.Payload(writer => writer.Write(value));

    [Fact]
    public void Deserialize_UnknownKeyBetweenTwoKnownOnes_LeavesBothUndisturbed()
    {
        byte[] payload = _serializer.Serialize(
            new ThreeKeys { First = 11, Middle = "retired", Last = 33 });

        var result = _serializer.Deserialize<OuterKeys>(payload)!;

        Assert.Equal(11, result.First);
        Assert.Equal(33, result.Last);
    }

    [Fact]
    public void Deserialize_UnknownFieldHoldingANestedGraph_IsSkippedWhole()
    {
        byte[] payload = _serializer.Serialize(new NestedMiddle
        {
            First = 11,
            Middle = new Dictionary<string, List<Node>>
            {
                ["a"] = [new Node { Value = 1 }, new Node { Value = 2 }],
                ["b"] = [new Node { Value = 3 }]
            },
            Last = 33
        });

        var result = _serializer.Deserialize<OuterKeys>(payload)!;

        Assert.Equal(11, result.First);
        Assert.Equal(33, result.Last);
    }

    [Fact]
    public void Deserialize_LargeUnknownField_IsSteppedOverInBoundedChunks()
    {
        // Read as V0 so the measurement sees the skip alone: the V1 envelope buffers the payload it
        // has just verified, which would hide what the skip itself costs.
        const int fieldLength = 4 * 1024 * 1024;

        byte[] payload =
            [.. Wire.NotNull, .. Wire.KeyedBody([new Wire.KeyedField(2, new byte[fieldLength])])];
        var serializer = Compact();

        AssertEx.AllocatesLessThan(64 * 1024, () => serializer.Deserialize<OuterKeys>(payload));
    }

    [Fact]
    public void Deserialize_UnknownFieldThatWouldFailToDecode_IsSkippedWithoutDecodingIt()
    {
        // The retired field declares a collection of int.MaxValue elements. Decoding it would be a
        // limit violation, so the read succeeding is what proves no formatter was asked for the type
        // that vanished with the field.
        byte[] hostileField = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(int.MaxValue);
        });

        byte[] payload = KeyedFrame(
            new Wire.KeyedField(1, Int32Field(11)),
            new Wire.KeyedField(2, hostileField),
            new Wire.KeyedField(3, Int32Field(33)));

        var result = _serializer.Deserialize<OuterKeys>(payload)!;

        Assert.Equal(11, result.First);
        Assert.Equal(33, result.Last);
    }

    [Fact]
    public void Deserialize_TruncatedUnknownField_ThrowsFormat()
    {
        byte[] payload = KeyedFrame(
            new Wire.KeyedField(1, Int32Field(11)),
            new Wire.KeyedField(2, [1, 2, 3, 4], DeclaredLength: 1024));

        AssertEx.Throws<BinaryFormatException>(
            "Key 2 payload", () => _serializer.Deserialize<OuterKeys>(payload));
    }

    [Fact]
    public void Deserialize_DuplicateKeyOnTheWire_ThrowsFormat()
    {
        byte[] payload = KeyedFrame(
            new Wire.KeyedField(1, Int32Field(11)),
            new Wire.KeyedField(1, Int32Field(22)));

        AssertEx.Throws<BinaryFormatException>(
            "Duplicate keyed field key 1", () => _serializer.Deserialize<OuterKeys>(payload));
    }

    [Fact]
    public void Deserialize_MalformedSevenBitKey_ThrowsFormat()
    {
        // Five continuation bytes: the encoding never admits a sixth.
        byte[] payload = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(1);
            writer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        }));

        AssertEx.Throws<BinaryFormatException>(
            "Malformed", () => _serializer.Deserialize<OuterKeys>(payload));
    }

    [Fact]
    public void Deserialize_KeyValueBoundaries_RoundTrip()
    {
        var source = new KeyBoundaries { Zero = 1, OneByte = 2, TwoBytes = 3, Largest = 4 };

        var result = _serializer.Deserialize<KeyBoundaries>(_serializer.Serialize(source))!;

        Assert.Equal(1, result.Zero);
        Assert.Equal(2, result.OneByte);
        Assert.Equal(3, result.TwoBytes);
        Assert.Equal(4, result.Largest);
    }

    [Fact]
    public void Serialize_KeyValueBoundaries_EncodesKeysAsSevenBitIntegers()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write7BitEncodedInt(4);
            writer.Write7BitEncodedInt(0); writer.Write(4); writer.Write(1);
            writer.Write7BitEncodedInt(127); writer.Write(4); writer.Write(2);
            writer.Write7BitEncodedInt(128); writer.Write(4); writer.Write(3);
            writer.Write7BitEncodedInt(int.MaxValue); writer.Write(4); writer.Write(4);
        });

        Assert.Equal(
            expected,
            serializer.Serialize(
                new KeyBoundaries { Zero = 1, OneByte = 2, TwoBytes = 3, Largest = 4 }));
    }

    [Fact]
    public void Deserialize_KnownFieldShorterThanItsMember_StopsAtTheWindowBoundary()
    {
        // Key 1 declares two bytes for an Int32 while key 3 follows it in full, so only the window
        // can be what stopped the read: the four bytes the member wants are physically there.
        byte[] payload = KeyedFrame(
            new Wire.KeyedField(1, [0x01, 0x02]),
            new Wire.KeyedField(3, Int32Field(33)));

        AssertEx.Throws<BinaryFormatException>(
            "Key 1 payload", () => _serializer.Deserialize<OuterKeys>(payload));
    }

    [Fact]
    public void Deserialize_KnownFieldLongerThanItsMember_ThrowsFormat()
    {
        byte[] payload = KeyedFrame(new Wire.KeyedField(1, [1, 2, 3, 4, 5, 6]));

        AssertEx.Throws<BinaryFormatException>(
            "trailing byte", () => _serializer.Deserialize<OuterKeys>(payload));
    }

    [Fact]
    public void Deserialize_TwoKeyedFields_ShareTheOperationElementBudget()
    {
        // Three elements per field, six in all, against a budget of five: the second field is over
        // the line only because the first one already spent against the same budget.
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithLimits(SerializationLimits.Default with { MaxTotalElements = 5 })
            .Build());

        byte[] payload = KeyedFrame(
            new Wire.KeyedField(1, ListOfThree()),
            new Wire.KeyedField(2, ListOfThree()));

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<TwoLists>(payload));

        static byte[] ListOfThree() => Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(3);
            writer.Write(1); writer.Write(2); writer.Write(3);
        });
    }

    [Fact]
    public void Deserialize_KeyedFieldReferringToItsOwnParent_SeesTheAncestor()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());
        var source = new KeyedCycle { Name = "root" };
        source.Self = source;

        var result = serializer.Deserialize<KeyedCycle>(serializer.Serialize(source))!;

        Assert.Equal("root", result.Name);
        Assert.Same(result, result.Self);
    }

    [Fact]
    public void Deserialize_UnionInsideAKeyedField_KeepsTheRuntimeType()
    {
        var source = new KeyedUnion { Shape = new UnionDerived { A = 1, Z = 2 }, Marker = 7 };

        var result = _serializer.Deserialize<KeyedUnion>(_serializer.Serialize(source))!;

        Assert.Equal(1, Assert.IsType<UnionDerived>(result.Shape).A);
        Assert.Equal(7, result.Marker);
    }

    [Fact]
    public void Deserialize_ObjectSharedBetweenSiblingKeyedFields_BecomesTwoInstances()
    {
        // The documented cost of skip tolerance: an id defined inside one field is invisible to the
        // next, so the object is written twice and read back as two.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());
        var shared = new Node { Value = 9 };

        var result = serializer.Deserialize<TwoNodes>(
            serializer.Serialize(new TwoNodes { A = shared, B = shared }))!;

        Assert.NotSame(result.A, result.B);
        Assert.Equal(9, result.A!.Value);
        Assert.Equal(9, result.B!.Value);
    }
}
