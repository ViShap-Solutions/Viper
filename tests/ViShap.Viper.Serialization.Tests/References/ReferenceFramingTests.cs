using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.References;

/// <summary>
/// Pins REF-05…REF-08, REF-12, REF-15, REF-16, REF-19 and REF-20: which values carry a reference
/// frame at all, the frame's bytes, what the payload does when a frame names nothing, and how far an
/// id is visible. The payload, not the local configuration, decides whether frames are there to be
/// read.
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
        byte[] expected =
        [
            .. Wire.ReferenceFrame(0, back: false),
            .. Wire.Payload(writer =>
            {
                writer.Write(3);
                writer.Write(4);
            })
        ];

        byte[] body = Body(_framed.Serialize(
            new StructHolder { Point = new PointStruct { X = 3, Y = 4 } }));

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Serialize_TheSameStructTwice_CarriesNoBackReference()
    {
        var point = new PointStruct { X = 3, Y = 4 };

        byte[] body = Body(_framed.Serialize(new List<PointStruct> { point, point }));

        byte[] expected =
        [
            .. Wire.ReferenceFrame(0, back: false),
            0x02,                                   // the count, as it is: the frame carries null
            .. Wire.Payload(writer =>
            {
                writer.Write(3); writer.Write(4);
                writer.Write(3); writer.Write(4);
            })
        ];

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Serialize_TheSameStringTwice_WritesItTwiceWithNoFrame()
    {
        string shared = new(['s', 'a', 'm', 'e']);

        byte[] expected =
        [
            .. Wire.ReferenceFrame(0, back: false),
            0x05, .. "same"u8,
            0x05, .. "same"u8
        ];

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
        byte[] payload = FramedFrame(Wire.ReferenceFrame(5, back: true));

        AssertEx.Throws<BinaryFormatException>(
            "was not found", () => _framed.Deserialize<Node>(payload));
    }

    // --- REF-20: the reference frame, byte for byte ----------------------------------------------

    [Fact]
    public void ReferenceFrame_IsZeroForNullAndTheIdTwiceWithTheBackBitPlusOne()
    {
        Assert.Equal<byte[]>([0x00], Wire.NullReference);
        Assert.Equal<byte[]>([0x01], Wire.ReferenceFrame(0, back: false));
        Assert.Equal<byte[]>([0x02], Wire.ReferenceFrame(0, back: true));
        Assert.Equal<byte[]>([0x0B], Wire.ReferenceFrame(5, back: false));
    }

    [Fact]
    public void Serialize_FirstOccurrencesAndABackReference_WriteTheDocumentedFrames()
    {
        // The list takes id 0 and its five nodes ids 1 to 5: the fifth node's frame is 0B.
        var nodes = Enumerable.Range(1, 5).Select(value => new Node { Value = value }).ToList();

        byte[] expected =
        [
            0x01,                                   // the list: first occurrence of id 0
            0x05,                                   // five elements
            0x03, 1, 0, 0, 0,
            0x05, 2, 0, 0, 0,
            0x07, 3, 0, 0, 0,
            0x09, 4, 0, 0, 0,
            0x0B, 5, 0, 0, 0                        // first occurrence of id 5
        ];

        Assert.Equal(expected, Body(_framed.Serialize(nodes)));

        // A node that refers to itself: its Next is a back reference to id 0.
        var cyclic = new Cyclic();
        cyclic.Next = cyclic;

        Assert.Equal<byte[]>([0x01, 0x01, 0x02], Body(_framed.Serialize(cyclic)));
    }

    [Fact]
    public void Deserialize_ANullReferenceFrame_IsNull()
    {
        Assert.Null(_framed.Deserialize<Node>(FramedFrame(Wire.NullReference)));
    }

    [Fact]
    public void Deserialize_BackReferenceAcrossTwoSiblingKeyedFields_ThrowsFormat()
    {
        // Key 1 defines id 1 inside its own scope. Key 2 is not a descendant of key 1, so the id it
        // names is no longer visible — which is exactly what stops a skipped field from dangling.
        byte[] first =
        [
            .. Wire.ReferenceFrame(1, back: false),
            .. Wire.Payload(writer => writer.Write(42))
        ];

        byte[] payload = FramedFrame(
        [
            .. Wire.ReferenceFrame(0, back: false),
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(1, first),
                new Wire.KeyedField(2, Wire.ReferenceFrame(1, back: true))
            ], nullFolded: false)
        ]);

        AssertEx.Throws<BinaryFormatException>(
            "was not found", () => _framed.Deserialize<TwoNodes>(payload));
    }

    [Fact]
    public void Serialize_ObjectSharedBetweenSiblingKeyedFields_EmitsNoBackReference()
    {
        var shared = new Node { Value = 9 };

        byte[] body = Body(_framed.Serialize(new TwoNodes { A = shared, B = shared }));

        // Both fields open the object afresh, as a first occurrence under an id of their own.
        byte[] expected =
        [
            .. Wire.ReferenceFrame(0, back: false),
            .. Wire.KeyedBody(
            [
                new Wire.KeyedField(
                    1,
                    [
                        .. Wire.ReferenceFrame(1, back: false),
                        .. Wire.Payload(writer => writer.Write(9))
                    ]),
                new Wire.KeyedField(
                    2,
                    [
                        .. Wire.ReferenceFrame(2, back: false),
                        .. Wire.Payload(writer => writer.Write(9))
                    ])
            ], nullFolded: false)
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
        byte[] frame = FramedFrame(
        [
            .. Wire.ReferenceFrame(0, back: false), // the root: first occurrence of id 0
            0x01,                                   // Name: empty
            .. Wire.ReferenceFrame(0, back: false), // Next: first occurrence again, under the root's id
            0x01,
            .. Wire.NullReference
        ]);

        AssertEx.Throws<BinaryFormatException>(
            "declared more than once", () => _framed.Deserialize<Cyclic>(frame));
    }

    [Fact]
    public void Deserialize_DistinctIdsForTheSameShape_IsUnaffected()
    {
        byte[] frame = FramedFrame(
        [
            .. Wire.ReferenceFrame(0, back: false),
            0x01,
            .. Wire.ReferenceFrame(1, back: false),
            0x01,
            .. Wire.NullReference
        ]);

        Assert.NotNull(_framed.Deserialize<Cyclic>(frame)?.Next);
    }

    // --- REF-19: a back reference resolves only to a value of the declared type ------------------

    [Fact]
    public void Deserialize_ABackReferenceToAnObjectOfAnotherType_ThrowsFormat()
    {
        // Id 1 is a Person; the list member then points back at it. The payload names an object the
        // member cannot hold, which is malformed input, not a cast for the caller to catch.
        byte[] body =
        [
            .. Wire.ReferenceFrame(0, back: false), // the root
            .. Wire.ReferenceFrame(1, back: false), // A: a Person
            30, 0, 0, 0,                            // Age
            0x06, .. "Alice"u8,                     // Name
            .. Wire.ReferenceFrame(1, back: true)   // B: back to the Person
        ];

        AssertEx.Throws<BinaryFormatException>(
            "resolves to", () => _plain.Deserialize<PersonThenList>(FramedFrame(body)));
    }

    /// <summary>A person, then a list: two members of unrelated reference types.</summary>
    public sealed class PersonThenList
    {
        public Person? A { get; set; }

        public List<int>? B { get; set; }
    }
}
