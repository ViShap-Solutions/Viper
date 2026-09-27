using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins V0-27: where a version 0 payload ends when it is read. It declares no length, so a span or a
/// sequence is by default exactly one payload, a seekable stream is read ahead and left where the root
/// value ends, and a stream that cannot seek is refused rather than read past the payload.
/// </summary>
public class V0BoundaryTests
{
    private static BinarySerializer V0 =>
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    private static Person Value => new() { Name = "Ada", Age = 36 };

    [Fact]
    public void Span_ExactlyOnePayload_IsRead()
    {
        var serializer = V0;

        Assert.Equal("Ada", serializer.Deserialize<Person>(serializer.Serialize(Value))!.Name);
    }

    [Fact]
    public void Span_WithBytesAfterTheRoot_ThrowsFormatByDefault()
    {
        var serializer = V0;
        byte[] payload = serializer.Serialize(Value);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>([.. payload, 0x00]));
        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(Sequences.Of<byte>(payload, [0x00])));
    }

    [Fact]
    public void SeekableStream_IsLeftWhereTheRootEnds()
    {
        var serializer = V0;
        byte[] payload = serializer.Serialize(Value);
        using var stream = new MemoryStream([.. payload, .. payload, 0xAA, 0xBB]);

        Assert.Equal("Ada", serializer.Deserialize<Person>(stream)!.Name);
        Assert.Equal(payload.Length, stream.Position);

        Assert.Equal("Ada", serializer.Deserialize<Person>(stream)!.Name);
        Assert.Equal(2 * payload.Length, stream.Position);
    }

    [Fact]
    public void SeekableStream_AKeyedPayloadWithBytesAfterIt_IsLeftWhereTheRootEnds()
    {
        var serializer = V0;
        byte[] payload = serializer.Serialize(new NestedSchema { Inner = new NewSchema { Kept = new Node { Value = 1 } }, Tag = "t" });
        using var stream = new MemoryStream([.. payload, 0x01, 0x02, 0x03]);

        Assert.Equal("t", serializer.Deserialize<NestedSchema>(stream)!.Tag);
        Assert.Equal(payload.Length, stream.Position);
    }

    [Fact]
    public void NonSeekableStream_ThrowsNotSupportedNamingTheRule()
    {
        var serializer = V0;

        var ex = Assert.Throws<NotSupportedException>(
            () => serializer.Deserialize<Person>(new NonSeekableStream(serializer.Serialize(Value))));

        Assert.Contains("carries no length", ex.Message, StringComparison.Ordinal);
    }
}
