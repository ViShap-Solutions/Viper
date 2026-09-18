using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-01, WF-03, WF-05, WF-10, WF-12, WF-14…WF-19 and V0-01: the byte-level encoding of
/// contract §22. These tests fail whenever the wire format changes, which is the point — a change
/// here is a compatibility break.
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
}
