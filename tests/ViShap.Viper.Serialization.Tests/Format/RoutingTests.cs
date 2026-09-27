using ViShap.Viper.Metadata;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins V0-13…V0-19: the reader identifies the wire format from the bytes the source has delivered
/// instead of guessing at it, so no source is asked to rewind, and the two formats carry the same
/// payload bytes for the same value.
/// </summary>
public class RoutingTests
{
    private static BinarySerializer V0 =>
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    [Fact]
    public void Deserialize_RecognizedButUnregisteredVersion_ThrowsFormatNotSupported()
    {
        // The magic and a version field are present, so the source is identified; nothing reads it.
        byte[] frame = Wire.FrameWith([42, 0, 0, 0], version: 0);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "No pipeline registered for format version 0",
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_NonSeekableSource_IsRoutedFromTheBytesItDelivered()
    {
        // The magic number is decoded from the bytes already taken, so the source never rewinds.
        byte[] frame = new BinarySerializer().Serialize(42);
        using var source = new FrameBoundStream(frame, boundary: frame.Length);

        Assert.Equal(42, new BinarySerializer().Deserialize<int>(source));
        Assert.Equal(frame.Length, source.Taken);
    }

    [Fact]
    public void Deserialize_V0PayloadAtANonZeroOffset_IsReadFromWhereTheStreamStood()
    {
        var serializer = V0;

        using var stream = new MemoryStream();
        stream.Write(new byte[100]);
        serializer.Serialize(stream, new Person { Name = "Ada", Age = 36 });
        stream.Position = 100;

        // The bytes that identified the format are the first bytes of the payload, not a probe.
        Assert.Equal("Ada", serializer.Deserialize<Person>(stream)!.Name);
    }

    [Fact]
    public void Deserialize_SourceThatFailsBeforeTheFormatIsKnown_ThrowsStream()
    {
        using var source = new FailingStream(bytesBeforeFailure: 2);

        var ex = AssertEx.Throws<BinaryStreamException>(
            "underlying stream", () => new BinarySerializer().Deserialize<int>(source));

        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public void Deserialize_V0PayloadShorterThanTheProbeWindow_IsNotMisrouted()
    {
        var serializer = V0;

        // Four bytes that happen to be the magic number: too few for the probe to match on.
        Assert.Equal(Wire.Magic, serializer.Deserialize<int>(serializer.Serialize(Wire.Magic)));
    }

    [Fact]
    public void Deserialize_V0PayloadWhoseFirstBytesNearlyMatchTheMagic_IsNotMisrouted()
    {
        var serializer = V0;
        var value = new PointStruct { X = Wire.Magic ^ 0x01, Y = 1 };

        var restored = serializer.Deserialize<PointStruct>(serializer.Serialize(value));

        Assert.Equal(value.X, restored.X);
        Assert.Equal(value.Y, restored.Y);
    }

    [Fact]
    public void Deserialize_V0PayloadThatOpensWithTheV1MagicAndVersion_IsRoutedToV1()
    {
        // Nothing in a V0 payload identifies it (§10.2), so bytes that collide with a V1 header are
        // read as one. This is a boundary of the headerless format, not a routing defect.
        var serializer = V0;
        byte[] payload = serializer.Serialize(new PointStruct { X = Wire.Magic, Y = 1 });

        Assert.Equal(Wire.Magic, BitConverter.ToInt32(payload));

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<PointStruct>(payload));
    }

    [Fact]
    public void Deserialize_CommittedV0Fixture_DecodesToTheDocumentedValue()
    {
        var person = V0.Deserialize<Person>(Wire.Fixture("person-v0.bin"))!;

        Assert.Equal("Ada", person.Name);
        Assert.Equal(36, person.Age);
    }

    [Fact]
    public void Deserialize_CommittedV1Fixture_DecodesToTheDocumentedValue()
    {
        var person = new BinarySerializer().Deserialize<Person>(Wire.Fixture("person-v1.bin"))!;

        Assert.Equal("Ada", person.Name);
        Assert.Equal(36, person.Age);
    }

    [Fact]
    public void Serialize_PositionalValue_ProducesTheSamePayloadBytesUnderBothFormats()
    {
        var person = new Person { Name = "Ada", Age = 36 };

        byte[] headerless = V0.Serialize(person);
        byte[] framed = new BinarySerializer().Serialize(person);

        Assert.Equal(headerless, framed[Wire.PlainHeaderLength..]);
        Assert.Equal(Wire.Fixture("person-v0.bin"), headerless);
    }

    [Fact]
    public void Serialize_KeyedContract_ProducesTheSamePayloadBytesUnderBothFormats()
    {
        var value = new OldSchema { Kept = new Node { Value = 5 } };

        byte[] headerless = V0.Serialize(value);
        byte[] framed = new BinarySerializer().Serialize(value);

        Assert.Equal(headerless, framed[Wire.PlainHeaderLength..]);
    }

    // --- the magic number is little-endian, whatever the host is ---------------------------------

    [Fact]
    public void Deserialize_AFrameWhoseMagicIsByteReversed_IsNotRecognized()
    {
        // Routing decodes the magic number little-endian by definition rather than in the host's byte
        // order, so its reversed spelling is not a Viper stream on any machine.
        byte[] reversed = new BinarySerializer().Serialize(42);
        Array.Reverse(reversed, 0, 4);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(reversed));
    }

    [Fact]
    public void Peek_AFrameWhoseMagicIsByteReversed_ReportsNoHeader()
    {
        byte[] reversed = new BinarySerializer().Serialize(42);
        Array.Reverse(reversed, 0, 4);

        using var source = new MemoryStream(reversed, writable: false);

        Assert.Null(BinaryFormatInspector.Peek(source));
    }
}
