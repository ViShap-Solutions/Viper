using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-01, WF-03, WF-05…WF-20 and V0-01: the byte-level framing of contract §22 — null flags,
/// reference markers, counts and the four shapes. These tests fail whenever the wire format
/// changes, which is the point — a change here is a compatibility break.
/// </summary>
public class WireFormatTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Header_HasTheDocumentedLayout()
    {
        byte[] payload = _serializer.Serialize(0x11223344);

        // int32 root: no null flag for a non-nullable value type, four payload bytes.
        Assert.Equal(Wire.Frame([0x44, 0x33, 0x22, 0x11]), payload);
    }

    [Fact]
    public void String_IsSevenBitLengthPrefixedUtf8()
    {
        byte[] payload = _serializer.Serialize("héllo");

        // null flag (reference type), then 7-bit length 6, then six UTF-8 bytes.
        Assert.Equal(Wire.Frame([1, 6, 0x68, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F]), payload);
    }

    [Fact]
    public void Sequence_IsInt32CountThenFramedElements()
    {
        byte[] payload = _serializer.Serialize(new List<int> { 1, 2 });

        Assert.Equal(
            Wire.Frame(
            [
                1,                      // non-null
                2, 0, 0, 0,             // count
                1, 0, 0, 0,             // element 0
                2, 0, 0, 0              // element 1
            ]),
            payload);
    }

    [Fact]
    public void Map_IsEntryCountThenKeyValuePairs()
    {
        byte[] payload = _serializer.Serialize(new Dictionary<int, int> { [7] = 9 });

        Assert.Equal(
            Wire.Frame(
            [
                1,                      // non-null
                1, 0, 0, 0,             // entry count
                7, 0, 0, 0,             // key
                9, 0, 0, 0              // value
            ]),
            payload);
    }

    [Fact]
    public void PositionalObject_WritesMembersInPlanOrder()
    {
        // Person has Name and Age; ordinal name order puts Age first.
        byte[] payload = _serializer.Serialize(new Person { Name = "A", Age = 2 });

        Assert.Equal(
            Wire.Frame(
            [
                1,                      // non-null root
                2, 0, 0, 0,             // Age
                1,                      // Name is non-null
                1, 0x41                 // length 1, "A"
            ]),
            payload);
    }

    [Fact]
    public void KeyedContract_WritesKeyLengthAndPayloadPerField()
    {
        byte[] payload = _serializer.Serialize(new OldSchema { Kept = new Node { Value = 5 } });

        Assert.Equal(
            Wire.Frame(
            [
                1,                      // non-null root
                1,                      // 7-bit field count
                2,                      // 7-bit key
                5, 0, 0, 0,             // int32 field payload length: null flag + int32
                1,                      // Node is non-null
                5, 0, 0, 0              // Node.Value
            ]),
            payload);
    }

    [Fact]
    public void ReferenceFraming_PrecedesTheShapeWhenPreserveReferencesIsOn()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());

        byte[] payload = serializer.Serialize(new List<int> { 1 });

        Assert.Equal(
            Wire.Frame(
                [
                    1,                  // non-null
                    0,                  // reference marker: first occurrence
                    0, 0, 0, 0,         // object id
                    1, 0, 0, 0,         // count
                    1, 0, 0, 0          // element 0
                ],
                preserveReferences: true),
            payload);
    }

    [Fact]
    public void Union_WritesTheTagBeforeMembers()
    {
        byte[] payload = _serializer.Serialize<UnionBase>(new UnionDerived { A = 3, Z = 4 });

        Assert.Equal(
            Wire.Frame(
            [
                1,                      // non-null root
                1,                      // union tag
                3, 0, 0, 0,             // A (ordinal name order: A before Z)
                4, 0, 0, 0              // Z
            ]),
            payload);
    }

    [Fact]
    public void V0_WritesThePayloadWithoutAHeader()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());

        Assert.Equal([0x44, 0x33, 0x22, 0x11], serializer.Serialize(0x11223344));
    }

    // --- WF-05, WF-06, WF-07: counts, optional strings and 7-bit integers -----------------------

    [Fact]
    public void Count_IsARawInt32RatherThanASevenBitInteger()
    {
        // 200 needs two bytes as a 7-bit integer and four as an int32.
        byte[] payload = Payload(new List<byte>(new byte[200]));

        Assert.Equal<byte[]>([1, 0xC8, 0x00, 0x00, 0x00], payload[..5]);
        Assert.Equal(5 + 200, payload.Length);
    }

    [Fact]
    public void Count_OfAnEmptySequence_IsFourZeroBytes()
    {
        Assert.Equal<byte[]>([1, 0, 0, 0, 0], Payload(new List<int>()));
    }

    [Fact]
    public void OptionalHeaderString_IsAPresentFlagThenTheString()
    {
        // The key id is the only optional string a caller can populate without a custom algorithm.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), new byte[32], keyId: "ab")
                .Build());

        byte[] frame = serializer.Serialize(42);

        Assert.Equal(0, _serializer.Serialize(42)[KeyIdOffset]);
        Assert.Equal<byte[]>([1, 2, 0x61, 0x62], frame[KeyIdOffset..(KeyIdOffset + 4)]);
    }

    [Theory]
    [InlineData(1, new byte[] { 0x01 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(16_383, new byte[] { 0xFF, 0x7F })]
    [InlineData(16_384, new byte[] { 0x80, 0x80, 0x01 })]
    public void SevenBitInteger_UsesTheShortestFormAndRoundTrips(int length, byte[] expectedPrefix)
    {
        string value = new('a', length);

        byte[] payload = Payload(value);

        Assert.Equal(expectedPrefix, payload[1..(1 + expectedPrefix.Length)]);
        Assert.Equal(1 + expectedPrefix.Length + length, payload.Length);
        Assert.Equal(value, _serializer.Deserialize<string>(_serializer.Serialize(value)));
    }

    // --- WF-08, WF-09: the null flag ------------------------------------------------------------

    [Fact]
    public void NullFlag_IsWrittenForReferenceTypesAndNullables()
    {
        Assert.Equal<byte[]>([1, 1, 0x61], Payload("a"));
        Assert.Equal<byte[]>([1, 0x2A, 0, 0, 0], Payload<int?>(42));
    }

    [Fact]
    public void NullFlag_IsAbsentForNonNullableValueTypes()
    {
        Assert.Equal<byte[]>([0x2A, 0, 0, 0], Payload(42));
        Assert.Equal<byte[]>([1, 0, 0, 0, 2, 0, 0, 0], Payload(new PointStruct { X = 1, Y = 2 }));
    }

    [Fact]
    public void NullFlag_SetToFalse_EndsTheValue()
    {
        Assert.Equal<byte[]>([0], Payload<string?>(null));
        Assert.Equal<byte[]>([0], Payload<int?>(null));
        Assert.Equal<byte[]>([0], Payload<Person?>(null));
        Assert.Equal<byte[]>([0], Payload<List<int>?>(null));
    }

    // --- WF-10…WF-13: the reference frame -------------------------------------------------------

    [Fact]
    public void ReferenceFrame_IsAMarkerByteAndAnInt32Id()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // non-null root
                0,                      // first occurrence
                0, 0, 0, 0,             // object id
                0x24, 0, 0, 0,          // Age
                1,                      // Name is non-null, and is not framed
                3, 0x41, 0x64, 0x61
            ],
            PreservingPayload(new Person { Name = "Ada", Age = 36 }));
    }

    [Fact]
    public void ReferenceFrame_IsAbsentWithoutPreserveReferences()
    {
        Assert.Equal<byte[]>(
            [1, 0x24, 0, 0, 0, 1, 3, 0x41, 0x64, 0x61],
            Payload(new Person { Name = "Ada", Age = 36 }));
    }

    [Fact]
    public void ReferenceFrame_IsNeverWrittenForScalarsOrValueTypes()
    {
        Assert.Equal<byte[]>([1, 1, 0x61], PreservingPayload("a"));
        Assert.Equal<byte[]>([0x2A, 0, 0, 0], PreservingPayload(42));
        Assert.Equal<byte[]>([1, 0, 0, 0, 2, 0, 0, 0], PreservingPayload(new PointStruct { X = 1, Y = 2 }));
        Assert.Equal<byte[]>([1, 0x2A, 0, 0, 0], PreservingPayload<int?>(42));
    }

    [Fact]
    public void ReferenceFrame_MarkerOneEndsTheValueAfterTheId()
    {
        var shared = new List<int> { 7 };

        Assert.Equal<byte[]>(
            [
                1, 0, 0, 0, 0, 0,       // root SharedLists: non-null, first occurrence, id 0
                1, 0, 1, 0, 0, 0,       // A: non-null, first occurrence, id 1
                1, 0, 0, 0,             // A count
                7, 0, 0, 0,             // A[0]
                1, 1, 1, 0, 0, 0        // B: non-null, back reference to id 1
            ],
            PreservingPayload(new SharedLists { A = shared, B = shared }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void Deserialize_UnknownReferenceMarker_ThrowsFormat(byte marker)
    {
        byte[] frame = Wire.Frame(
            Wire.Payload(writer =>
            {
                writer.Write(true);     // non-null
                writer.Write(marker);
                writer.Write(0);        // object id
            }),
            preserveReferences: true);

        AssertEx.Throws<BinaryFormatException>(
            $"Unknown reference marker {marker}",
            () => new BinarySerializer().Deserialize<List<int>>(frame));
    }

    // --- WF-18, WF-20: the keyed layout ---------------------------------------------------------

    [Fact]
    public void KeyedContract_WritesFieldsInAscendingKeyOrder()
    {
        // Late is declared before Early but carries the higher key.
        Assert.Equal<byte[]>(
            [
                1,                      // non-null root
                2,                      // field count
                1,                      // key 1: Early
                4, 0, 0, 0,
                0x0B, 0, 0, 0,
                5,                      // key 5: Late
                4, 0, 0, 0,
                0x16, 0, 0, 0
            ],
            Payload(new UnsortedKeys { Early = 11, Late = 22 }));
    }

    [Fact]
    public void Deserialize_KeyedFieldWithTrailingBytesInsideIt_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(KeyedNode(declaredLength: 6, junkBytes: 1));

        AssertEx.Throws<BinaryFormatException>(
            "trailing byte(s)", () => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    [Fact]
    public void Deserialize_KeyedFieldShorterThanTheValueItHolds_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(KeyedNode(declaredLength: 3, junkBytes: 0));

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<OldSchema>(frame));
    }

    /// <summary>Byte offset of the key-id present flag in a header with no custom algorithm names.</summary>
    private const int KeyIdOffset = 14;

    /// <summary>The payload bytes of a V1 frame, with the fixed-length header removed.</summary>
    private byte[] Payload<T>(T value) => _serializer.Serialize(value)[Wire.PlainHeaderLength..];

    private static byte[] PreservingPayload<T>(T value) =>
        new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build())
            .Serialize(value)[Wire.PlainHeaderLength..];

    /// <summary>
    /// An <c>OldSchema</c> payload whose single field declares <paramref name="declaredLength"/>
    /// bytes and is followed by <paramref name="junkBytes"/> bytes inside that window.
    /// </summary>
    private static byte[] KeyedNode(int declaredLength, int junkBytes) =>
        Wire.Payload(writer =>
        {
            writer.Write(true);                 // non-null root
            writer.Write7BitEncodedInt(1);      // one field
            writer.Write7BitEncodedInt(2);      // key 2: Kept
            writer.Write(declaredLength);
            writer.Write(true);                 // Node is non-null
            writer.Write(5);                    // Node.Value
            writer.Write(new byte[junkBytes]);
        });
}
