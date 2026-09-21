using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.References;

/// <summary>
/// Pins REF-05…REF-08 and REF-12…REF-16: which values carry a reference frame at all, what the
/// payload does when the frame is nonsense, and how far an id is visible. The payload, not the local
/// configuration, decides whether frames are there to be read.
/// </summary>
public class ReferenceFramingTests
{
    private readonly BinarySerializer _plain = new();

    private readonly BinarySerializer _framed =
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    private static byte[] Body(byte[] frame) => frame[Wire.ReadHeader(frame).HeaderLength..];

    private static byte[] FramedFrame(byte[] body) => Wire.Frame(body, preserveReferences: true);

    [Fact]
    public void Serialize_ValueTypedMember_CarriesNoReferenceFrame()
    {
        // The root object is framed; the struct behind it is not, because a value type cannot be
        // shared and a box of it would be a different object every time.
        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write((byte)0);
            writer.Write(0);
            writer.Write(3);
            writer.Write(4);
        });

        byte[] body = Body(_framed.Serialize(
            new StructHolder { Point = new PointStruct { X = 3, Y = 4 } }));

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Serialize_TheSameStructTwice_CarriesNoBackReference()
    {
        var point = new PointStruct { X = 3, Y = 4 };

        byte[] body = Body(_framed.Serialize(new List<PointStruct> { point, point }));

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write((byte)0);
            writer.Write(0);
            writer.Write(2);
            writer.Write(3); writer.Write(4);
            writer.Write(3); writer.Write(4);
        });

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Serialize_TheSameStringTwice_WritesItTwiceWithNoFrame()
    {
        string shared = new(['s', 'a', 'm', 'e']);

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write((byte)0);
            writer.Write(0);
            writer.Write(true); writer.Write("same");
            writer.Write(true); writer.Write("same");
        });

        byte[] body = Body(_framed.Serialize(new SharedStrings { A = shared, B = shared }));

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Deserialize_FramedPayloadThroughAPlainSerializer_StillPreservesIdentity()
    {
        // The header records how the payload was written, so the reader follows it rather than its
        // own configuration.
        var shared = new List<int> { 1, 2, 3 };

        byte[] payload = _framed.Serialize(new SharedLists { A = shared, B = shared });
        var result = _plain.Deserialize<SharedLists>(payload)!;

        Assert.True(Wire.ReadHeader(payload).PreserveReferences);
        Assert.Same(result.A, result.B);
    }

    [Fact]
    public void Deserialize_PlainPayloadThroughAFramingSerializer_StillReadsUnframedValues()
    {
        var shared = new List<int> { 1, 2, 3 };

        byte[] payload = _plain.Serialize(new SharedLists { A = shared, B = shared });
        var result = _framed.Deserialize<SharedLists>(payload)!;

        Assert.False(Wire.ReadHeader(payload).PreserveReferences);
        Assert.NotSame(result.A, result.B);
        Assert.Equal(result.A, result.B);
    }

    [Fact]
    public void Deserialize_UnknownReferenceId_ThrowsFormat()
    {
        byte[] payload = FramedFrame([.. Wire.NotNull, .. Wire.ReferenceFrame(1, 5)]);

        AssertEx.Throws<BinaryFormatException>(
            "was not found", () => _framed.Deserialize<Node>(payload));
    }

    [Fact]
    public void Deserialize_NegativeReferenceId_ThrowsFormat()
    {
        byte[] payload = FramedFrame([.. Wire.NotNull, .. Wire.ReferenceFrame(0, -1)]);

        AssertEx.Throws<BinaryFormatException>(
            "must be non-negative", () => _framed.Deserialize<Node>(payload));
    }

    [Fact]
    public void Deserialize_InvalidReferenceMarker_ThrowsFormat()
    {
        byte[] payload = FramedFrame([.. Wire.NotNull, .. Wire.ReferenceFrame(7, 0)]);

        AssertEx.Throws<BinaryFormatException>(
            "Unknown reference marker 7", () => _framed.Deserialize<Node>(payload));
    }

    [Fact]
    public void Deserialize_BackReferenceAcrossTwoSiblingKeyedFields_ThrowsFormat()
    {
        // Key 1 defines id 1 inside its own scope. Key 2 is not a descendant of key 1, so the id it
        // names is no longer visible — which is exactly what stops a skipped field from dangling.
        byte[] first =
        [
            .. Wire.NotNull, .. Wire.ReferenceFrame(0, 1),
            .. Wire.Payload(writer => writer.Write(42))
        ];

        byte[] payload = FramedFrame(
        [
            .. Wire.NotNull, .. Wire.ReferenceFrame(0, 0),
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(1, first),
                new Wire.KeyedField(2, [.. Wire.NotNull, .. Wire.ReferenceFrame(1, 1)])
            ])
        ]);

        AssertEx.Throws<BinaryFormatException>(
            "was not found", () => _framed.Deserialize<TwoNodes>(payload));
    }

    [Fact]
    public void Serialize_ObjectSharedBetweenSiblingKeyedFields_EmitsNoBackReference()
    {
        var shared = new Node { Value = 9 };

        byte[] body = Body(_framed.Serialize(new TwoNodes { A = shared, B = shared }));

        // Both fields open the object afresh, with marker 0 and an id of their own.
        byte[] expected =
        [
            .. Wire.NotNull, .. Wire.ReferenceFrame(0, 0),
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(
                    1,
                    [
                        .. Wire.NotNull, .. Wire.ReferenceFrame(0, 1),
                        .. Wire.Payload(writer => writer.Write(9))
                    ]),
                new Wire.KeyedField(
                    2,
                    [
                        .. Wire.NotNull, .. Wire.ReferenceFrame(0, 2),
                        .. Wire.Payload(writer => writer.Write(9))
                    ])
            ])
        ];

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Deserialize_TwoEqualButDistinctObjects_AreNotMergedByEquals()
    {
        // ValueEqualNode calls two instances equal. Identity, not equality, decides what is shared.
        var source = new EqualNodePair
        {
            A = new ValueEqualNode { Value = 5 },
            B = new ValueEqualNode { Value = 5 }
        };

        var result = _framed.Deserialize<EqualNodePair>(_framed.Serialize(source))!;

        Assert.NotSame(result.A, result.B);
        Assert.Equal(result.A, result.B);
    }

    [Fact]
    public void Deserialize_OneInstanceInBothSlots_IsRestoredAsOne()
    {
        var node = new ValueEqualNode { Value = 5 };

        var result = _framed.Deserialize<EqualNodePair>(
            _framed.Serialize(new EqualNodePair { A = node, B = node }))!;

        Assert.Same(result.A, result.B);
    }

    // --- an identifier is declared once ----------------------------------------------------------

    [Fact]
    public void Deserialize_TwoFirstOccurrencesUnderOneId_ThrowsFormat()
    {
        // Two first occurrences under one id would give a single graph a second spelling, and the
        // later one would quietly replace the object earlier references already resolve to.
        byte[] frame = FramedFrame(Wire.Payload(writer =>
        {
            writer.Write(true);             // the root is present
            writer.Write((byte)0);          // first occurrence
            writer.Write(0);                // id 0
            writer.Write(string.Empty);     // Name
            writer.Write(true);             // Next is present
            writer.Write((byte)0);          // first occurrence again
            writer.Write(0);                // under the id the root already holds
            writer.Write(string.Empty);
            writer.Write(false);
        }));

        AssertEx.Throws<BinaryFormatException>(
            "declared more than once", () => _framed.Deserialize<Cyclic>(frame));
    }

    [Fact]
    public void Deserialize_DistinctIdsForTheSameShape_IsUnaffected()
    {
        byte[] frame = FramedFrame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write((byte)0);
            writer.Write(0);
            writer.Write(string.Empty);
            writer.Write(true);
            writer.Write((byte)0);
            writer.Write(1);
            writer.Write(string.Empty);
            writer.Write(false);
        }));

        Assert.NotNull(_framed.Deserialize<Cyclic>(frame)?.Next);
    }
}
