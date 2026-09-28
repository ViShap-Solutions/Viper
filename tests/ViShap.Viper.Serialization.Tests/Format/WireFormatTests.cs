using System.Collections;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-01, WF-03, WF-05…WF-12, WF-14…WF-20, WF-31…WF-37 and V0-01: the byte-level framing of
/// contract §22 — the header, null folded into the first number of a value, the reference frame,
/// counts and the four shapes. The expected bytes are the examples the format was specified with.
/// These tests fail whenever the wire format changes, which is the point — a change here is a
/// compatibility break.
/// </summary>
public class WireFormatTests
{
    private readonly BinarySerializer _serializer = new();

    // --- HDR-01: the header ---------------------------------------------------------------------

    [Fact]
    public void Header_HasTheDocumentedLayout()
    {
        byte[] payload = _serializer.Serialize(0x11223344);

        // magic, version 1, payload mode 0, no service, onDiskLength 4, then the int32 root.
        Assert.Equal<byte[]>(
            [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00, 0x04, 0x44, 0x33, 0x22, 0x11],
            payload);
    }

    [Fact]
    public void Header_SmallestFrame_IsTheEmptyStringAfterEightHeaderBytes()
    {
        Assert.Equal<byte[]>(
            [0x42, 0x53, 0x45, 0x52, 0x01, 0x00, 0x00, 0x01, 0x01],
            _serializer.Serialize(string.Empty));
    }

    [Fact]
    public void Header_BrotliAndAesGcmWithAKeyId_HasTheDocumentedServiceRecords()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new BrotliCompression())
                .WithEncryption(new Aes256GcmEncryption(), new byte[32], keyId: "k7")
                .Build());

        // 998 bytes: the count 998 + 1 takes two bytes, so the payload is exactly 1 000 bytes.
        byte[] frame = serializer.Serialize(new byte[998]);
        var header = Wire.ReadHeader(frame);

        Assert.Equal<byte[]>(
        [
            0x42, 0x53, 0x45, 0x52,         // magic
            0x01,                           // version
            0x00,                           // payload mode
            0x02,                           // two service records
            0x05, 0x03, 0x02, 0xE8, 0x07,   // compression: Brotli, uncompressed 1000
            0x07, 0x04, 0x01, 0x03, 0x6B, 0x37 // encryption: AES-GCM, KeyId "k7"
        ], frame[..header.OnDiskLengthOffset]);

        Assert.Equal(frame.Length - header.HeaderLength, header.OnDiskLength);
        Assert.Equal(new byte[998], serializer.Deserialize<byte[]>(frame));
    }

    [Fact]
    public void Header_PayloadModeCarriesTheReferencesBit()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build());

        Assert.Equal(0x01, serializer.Serialize(42)[Wire.ModeOffset]);
        Assert.Equal(0x00, _serializer.Serialize(42)[Wire.ModeOffset]);
    }

    // --- WF-03, WF-31: strings carry their null in the length ----------------------------------

    [Fact]
    public void String_IsItsUtf8LengthPlusOneThenTheBytes()
    {
        // 6 UTF-8 bytes, so the length is written as 7.
        Assert.Equal<byte[]>([7, 0x68, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F], Payload("héllo"));
    }

    [Fact]
    public void String_NullEmptyAndHello_AreTheDocumentedBytes()
    {
        Assert.Equal<byte[]>([0x00], Payload<string?>(null));
        Assert.Equal<byte[]>([0x01], Payload(string.Empty));
        Assert.Equal<byte[]>([0x06, 0x68, 0x65, 0x6C, 0x6C, 0x6F], Payload("hello"));
    }

    // --- WF-14, WF-15, WF-32: sequences and maps ------------------------------------------------

    [Fact]
    public void Sequence_IsItsCountPlusOneThenFramedElements()
    {
        Assert.Equal<byte[]>(
            [
                3,                      // count 2, plus one: not null
                1, 0, 0, 0,             // element 0
                2, 0, 0, 0              // element 1
            ],
            Payload(new List<int> { 1, 2 }));
    }

    [Fact]
    public void Map_IsItsEntryCountPlusOneThenKeyValuePairs()
    {
        Assert.Equal<byte[]>(
            [
                2,                      // one entry, plus one
                7, 0, 0, 0,             // key
                9, 0, 0, 0              // value
            ],
            Payload(new Dictionary<int, int> { [7] = 9 }));
    }

    [Fact]
    public void Sequence_OfThreeWithReferencesOffAndOn_IsTheDocumentedBytes()
    {
        var list = new List<int> { 1, 2, 3 };

        Assert.Equal<byte[]>([0x04, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0], Payload(list));

        // With references the frame carries null, so the count is written as it is.
        Assert.Equal<byte[]>([0x01, 0x03, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0], PreservingPayload(list));
    }

    [Fact]
    public void ByteArray_IsAnOrdinarySequence()
    {
        Assert.Equal<byte[]>([0x03, 0x0A, 0x0B], Payload(new byte[] { 0x0A, 0x0B }));
        Assert.Equal<byte[]>([0x01, 0x02, 0x0A, 0x0B], PreservingPayload(new byte[] { 0x0A, 0x0B }));
        Assert.Equal<byte[]>([0x00], Payload<byte[]?>(null));
    }

    // --- WF-05, WF-07: structural numbers are minimal 7-bit integers ----------------------------

    [Fact]
    public void Count_IsASevenBitIntegerCarryingNull()
    {
        // 200 elements: the count 201 needs two bytes.
        byte[] payload = Payload(new List<byte>(new byte[200]));

        Assert.Equal<byte[]>([0xC9, 0x01], payload[..2]);
        Assert.Equal(2 + 200, payload.Length);
    }

    [Fact]
    public void Count_OfAnEmptySequence_IsOne()
    {
        Assert.Equal<byte[]>([0x01], Payload(new List<int>()));
    }

    [Theory]
    [InlineData(0, new byte[] { 0x01 })]
    [InlineData(126, new byte[] { 0x7F })]
    [InlineData(127, new byte[] { 0x80, 0x01 })]
    [InlineData(16_382, new byte[] { 0xFF, 0x7F })]
    [InlineData(16_383, new byte[] { 0x80, 0x80, 0x01 })]
    public void SevenBitInteger_UsesTheShortestFormAndRoundTrips(int length, byte[] expectedPrefix)
    {
        string value = new('a', length);

        byte[] payload = Payload(value);

        Assert.Equal(expectedPrefix, payload[..expectedPrefix.Length]);
        Assert.Equal(expectedPrefix.Length + length, payload.Length);
        Assert.Equal(value, _serializer.Deserialize<string>(_serializer.Serialize(value)));
    }

    // --- WF-06: the key id of the encryption record ---------------------------------------------

    [Theory]
    [InlineData("ab", new byte[] { 0x01, 0x03, 0x61, 0x62 })]
    [InlineData("", new byte[] { 0x01, 0x01 })]
    [InlineData(null, new byte[] { 0x01, 0x00 })]
    public void KeyId_IsFoldedWithItsAbsenceAfterTheAlgorithmId(string? keyId, byte[] expectedBody)
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256GcmEncryption(), new byte[32], keyId: keyId)
                .Build());

        byte[] frame = serializer.Serialize(42);
        var record = Wire.ReadHeader(frame).Service(Wire.EncryptionService);

        Assert.Equal(expectedBody, frame[record.BodyOffset..(record.BodyOffset + record.BodyLength)]);
    }

    // --- WF-08, WF-09, WF-33…WF-36: where null lives --------------------------------------------

    [Fact]
    public void Null_OfEveryDeclaredTypeThatCanBeNull_IsASingleZero()
    {
        Assert.Equal<byte[]>([0], Payload<string?>(null));
        Assert.Equal<byte[]>([0], Payload<int?>(null));
        Assert.Equal<byte[]>([0], Payload<Person?>(null));
        Assert.Equal<byte[]>([0], Payload<List<int>?>(null));
        Assert.Equal<byte[]>([0], Payload<Dictionary<int, int>?>(null));
        Assert.Equal<byte[]>([0], Payload<OldSchema?>(null));
        Assert.Equal<byte[]>([0], Payload<UnionBase?>(null));
        Assert.Equal<byte[]>([0], PreservingPayload<Person?>(null));
        Assert.Equal<byte[]>([0], PreservingPayload<List<int>?>(null));
    }

    [Fact]
    public void Nullable_IsAFlagThenTheValue()
    {
        Assert.Equal<byte[]>([0x01, 0x05, 0x00, 0x00, 0x00], Payload<int?>(5));
        Assert.Equal<byte[]>([0x01, 0x05, 0x00, 0x00, 0x00], PreservingPayload<int?>(5));
    }

    [Fact]
    public void TypeThatCannotBeNull_CarriesNothingForIt()
    {
        Assert.Equal<byte[]>([0x2A, 0, 0, 0], Payload(42));
        Assert.Equal<byte[]>([1, 0, 0, 0, 2, 0, 0, 0], Payload(new PointStruct { X = 1, Y = 2 }));
    }

    [Fact]
    public void PositionalObject_CarriesAFlagAndWritesMembersInPlanOrder()
    {
        // Person has Name and Age; ordinal name order puts Age first.
        Assert.Equal<byte[]>(
            [
                1,                      // flag: not null
                2, 0, 0, 0,             // Age
                2, 0x41                 // Name: length 1 + 1, "A"
            ],
            Payload(new Person { Name = "A", Age = 2 }));
    }

    [Fact]
    public void KeyedClass_CarriesItsNullInTheFieldCount()
    {
        Assert.Equal<byte[]>(
            [
                2,                      // field count 1, plus one: not null
                2,                      // key 2
                5, 0, 0, 0,             // int32 field payload length: Node's flag + int32
                1,                      // Node is not null
                5, 0, 0, 0              // Node.Value
            ],
            Payload(new OldSchema { Kept = new Node { Value = 5 } }));
    }

    [Fact]
    public void KeyedStruct_WritesItsFieldCountAsItIs()
    {
        Assert.Equal<byte[]>(
            [
                2,                      // two fields; a struct cannot be null
                1, 4, 0, 0, 0, 3, 0, 0, 0,
                2, 4, 0, 0, 0, 4, 0, 0, 0
            ],
            Payload(new KeyedPoint { X = 3, Y = 4 }));
    }

    [Fact]
    public void KeyedClass_UnderReferences_WritesItsFieldCountAsItIsAfterTheFrame()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // first occurrence of id 0
                1,                      // field count 1, without the fold
                2, 5, 0, 0, 0,          // key 2, length
                3,                      // Node: first occurrence of id 1
                5, 0, 0, 0
            ],
            PreservingPayload(new OldSchema { Kept = new Node { Value = 5 } }));
    }

    [Fact]
    public void Union_CarriesAFlagThenTheTagBeforeMembers()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // flag: not null
                1,                      // union tag
                3, 0, 0, 0,             // A (ordinal name order: A before Z)
                4, 0, 0, 0              // Z
            ],
            Payload<UnionBase>(new UnionDerived { A = 3, Z = 4 }));
    }

    [Fact]
    public void Union_UnderReferences_CarriesTheFrameThenTheTag()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // first occurrence of id 0
                1,                      // union tag
                3, 0, 0, 0,
                4, 0, 0, 0
            ],
            PreservingPayload<UnionBase>(new UnionDerived { A = 3, Z = 4 }));
    }

    [Fact]
    public void KeyedContract_WritesFieldsInAscendingKeyOrder()
    {
        // Late is declared before Early but carries the higher key.
        Assert.Equal<byte[]>(
            [
                3,                      // field count 2, plus one
                1,                      // key 1: Early
                4, 0, 0, 0,
                0x0B, 0, 0, 0,
                5,                      // key 5: Late
                4, 0, 0, 0,
                0x16, 0, 0, 0
            ],
            Payload(new UnsortedKeys { Early = 11, Late = 22 }));
    }

    // --- WF-10…WF-12: the reference frame -------------------------------------------------------

    [Fact]
    public void ReferenceFrame_IsOneSevenBitIntegerBeforeTheShape()
    {
        Assert.Equal<byte[]>(
            [
                1,                      // first occurrence of id 0
                0x24, 0, 0, 0,          // Age
                4, 0x41, 0x64, 0x61     // Name: never framed
            ],
            PreservingPayload(new Person { Name = "Ada", Age = 36 }));
    }

    [Fact]
    public void ReferenceFrame_IsAbsentWithoutPreserveReferences()
    {
        Assert.Equal<byte[]>(
            [1, 0x24, 0, 0, 0, 4, 0x41, 0x64, 0x61],
            Payload(new Person { Name = "Ada", Age = 36 }));
    }

    [Fact]
    public void ReferenceFrame_IsNeverWrittenForScalarsOrValueTypes()
    {
        Assert.Equal<byte[]>([2, 0x61], PreservingPayload("a"));
        Assert.Equal<byte[]>([0x2A, 0, 0, 0], PreservingPayload(42));
        Assert.Equal<byte[]>([1, 0, 0, 0, 2, 0, 0, 0], PreservingPayload(new PointStruct { X = 1, Y = 2 }));
        Assert.Equal<byte[]>([1, 0x2A, 0, 0, 0], PreservingPayload<int?>(42));
    }

    [Fact]
    public void ReferenceFrame_BackReferenceEndsTheValue()
    {
        var shared = new List<int> { 7 };

        Assert.Equal<byte[]>(
            [
                0x01,                   // root SharedLists: first occurrence of id 0
                0x03,                   // A: first occurrence of id 1
                0x01,                   // A count, as it is
                7, 0, 0, 0,             // A[0]
                0x04                    // B: back reference to id 1
            ],
            PreservingPayload(new SharedLists { A = shared, B = shared }));
    }

    // --- WF-20: the keyed layout ----------------------------------------------------------------

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

    // --- WF-37: the types the table of §22.2 names by the general rule -------------------------

    [Fact]
    public void StringEncodedScalar_FoldsItsNullIntoTheStringLength()
    {
        Assert.Equal<byte[]>([0x02, 0x61], Payload(new Uri("a", UriKind.Relative)));
        Assert.Equal<byte[]>([0x00], Payload<Uri?>(null));
        Assert.Equal<byte[]>([0x02, 0x61], PreservingPayload(new Uri("a", UriKind.Relative)));
    }

    [Fact]
    public void BitArray_FoldsItsNullIntoTheBitCount()
    {
        var bits = new BitArray([true, false, true, true, false, false, false, true, true]);

        Assert.Equal<byte[]>([0x0A, 0x02, 0x8D, 0x01], Payload(bits));
        Assert.Equal<byte[]>([0x00], Payload<BitArray?>(null));
    }

    [Fact]
    public void MultiDimensionalArray_FoldsItsNullIntoTheRank()
    {
        var grid = new[,] { { 1, 2, 3 }, { 4, 5, 6 } };
        byte[] elements = [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0, 5, 0, 0, 0, 6, 0, 0, 0];

        Assert.Equal<byte[]>([0x03, 0x02, 0x03, .. elements], Payload(grid));
        Assert.Equal<byte[]>([0x01, 0x02, 0x02, 0x03, .. elements], PreservingPayload(grid));
        Assert.Equal<byte[]>([0x00], Payload<int[,]?>(null));
    }

    [Fact]
    public void Tuple_BeginsWithNoNumberAndCarriesAFlag()
    {
        Assert.Equal<byte[]>([0x01, 8, 0, 0, 0, 0x06, .. "eight"u8], Payload(Tuple.Create(8, "eight")));
        Assert.Equal<byte[]>([0x00], Payload<Tuple<int, string>?>(null));
        Assert.Equal<byte[]>([0x01, 8, 0, 0, 0, 0x06, .. "eight"u8], PreservingPayload(Tuple.Create(8, "eight")));
    }

    // --- V0-01 ----------------------------------------------------------------------------------

    [Fact]
    public void V0_WritesThePayloadWithoutAHeader()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());

        Assert.Equal([0x44, 0x33, 0x22, 0x11], serializer.Serialize(0x11223344));
    }

    /// <summary>The payload bytes of a V1 frame, with the header removed.</summary>
    private byte[] Payload<T>(T value) => Wire.Body(_serializer.Serialize(value));

    private static byte[] PreservingPayload<T>(T value) =>
        Wire.Body(new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build())
            .Serialize(value));

    /// <summary>
    /// An <c>OldSchema</c> payload whose single field declares <paramref name="declaredLength"/>
    /// bytes and is followed by <paramref name="junkBytes"/> bytes inside that window.
    /// </summary>
    private static byte[] KeyedNode(int declaredLength, int junkBytes) =>
        Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(2);      // one field, plus one: not null
            writer.Write7BitEncodedInt(2);      // key 2: Kept
            writer.Write(declaredLength);
            writer.Write(true);                 // Node is not null
            writer.Write(5);                    // Node.Value
            writer.Write(new byte[junkBytes]);
        });
}
