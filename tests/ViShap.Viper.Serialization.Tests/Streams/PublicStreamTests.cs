using ViShap.Viper.Metadata;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Streams;

/// <summary>
/// Pins STR-23…STR-28: how the public entry points behave against the streams a caller really owns
/// — seekable and not, short-reading, truncated, read-only and write-only — and where position
/// restoration is promised.
/// </summary>
public class PublicStreamTests
{
    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    // --- STR-23: the ordinary case ---------------------------------------------------------------

    [Fact]
    public void Serialize_Deserialize_OverASeekableMemoryStream_RoundTrips()
    {
        var serializer = new BinarySerializer();
        using var stream = new MemoryStream();

        serializer.Serialize(stream, Sample());
        stream.Position = 0;
        var restored = serializer.Deserialize<Person>(stream);

        Assert.Equal("Alice", restored!.Name);
        Assert.Equal(30, restored.Age);
    }

    [Fact]
    public void Deserialize_FromTheMiddleOfALargerStream_ReadsOnlyItsOwnPayload()
    {
        var serializer = new BinarySerializer();
        byte[] payload = serializer.Serialize(Sample());
        using var stream = new MemoryStream();
        stream.Write(new byte[32]);
        stream.Write(payload);
        stream.Write(new byte[32]);
        stream.Position = 32;

        var restored = serializer.Deserialize<Person>(stream);

        Assert.Equal("Alice", restored!.Name);
        Assert.Equal(32 + payload.Length, stream.Position);
    }

    // --- STR-24: only the APIs that need seekability demand it -----------------------------------

    [Fact]
    public void Deserialize_FromANonSeekableSource_ThrowsNotSupported()
    {
        using var source = new NonSeekableStream(new BinarySerializer().Serialize(Sample()));

        Assert.Throws<NotSupportedException>(() => new BinarySerializer().Deserialize<Person>(source));
    }

    [Fact]
    public void Peek_OverANonSeekableSource_ThrowsNotSupported()
    {
        using var source = new NonSeekableStream(new BinarySerializer().Serialize(Sample()));

        Assert.Throws<NotSupportedException>(() => BinaryFormatInspector.Peek(source));
    }

    [Fact]
    public void Serialize_IntoANonSeekableDestination_Succeeds()
    {
        // Writing V1 buffers the payload, so the destination is only ever appended to.
        var serializer = new BinarySerializer();
        using var destination = new NonSeekableWriteStream();

        serializer.Serialize(destination, Sample());

        Assert.Equal(serializer.Serialize(Sample()), destination.Written);
    }

    [Fact]
    public void Serialize_AKeyedContractIntoANonSeekableV0Destination_ThrowsNotSupported()
    {
        // V0 writes straight through, and a keyed field length is patched after the field is
        // written, so that one combination genuinely needs to seek (§10.2, §14.2).
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());
        using var destination = new NonSeekableWriteStream();

        Assert.Throws<NotSupportedException>(
            () => serializer.Serialize(destination, new NewSchema { Kept = new Node { Value = 1 } }));
    }

    [Fact]
    public void Serialize_APositionalGraphIntoANonSeekableV0Destination_Succeeds()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());
        using var destination = new NonSeekableWriteStream();

        serializer.Serialize(destination, Sample());

        Assert.NotEmpty(destination.Written);
    }

    // --- STR-25: a source that answers in small pieces -------------------------------------------

    [Fact]
    public void Deserialize_FromAShortReadingSource_RoundTrips()
    {
        var serializer = new BinarySerializer();
        var original = new Person { Name = new string('n', 300), Age = 7 };
        using var source = new PartialReadStream(serializer.Serialize(original), chunkSize: 5);

        var restored = serializer.Deserialize<Person>(source);

        Assert.Equal(original.Name, restored!.Name);
        Assert.Equal(7, restored.Age);
    }

    [Fact]
    public void Deserialize_FromASourceYieldingOneByteAtATime_RoundTrips()
    {
        var serializer = new BinarySerializer();
        using var source = new PartialReadStream(serializer.Serialize(Sample()), chunkSize: 1);

        Assert.Equal("Alice", serializer.Deserialize<Person>(source)!.Name);
    }

    // --- STR-26: a source that ends too early ----------------------------------------------------

    [Fact]
    public void Deserialize_FromATruncatedSource_ThrowsFormat()
    {
        var serializer = new BinarySerializer();
        byte[] payload = serializer.Serialize(Sample());
        using var source = new MemoryStream(Mutate.Truncate(payload, payload.Length - 3), writable: false);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>(source));
    }

    [Fact]
    public void Deserialize_FromAnEmptySource_ThrowsFormat()
    {
        using var source = new MemoryStream();

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<Person>(source));
    }

    // --- STR-27: a source that cannot be read, a destination that cannot be written --------------

    [Fact]
    public void Deserialize_FromAWriteOnlySource_ThrowsNotSupported()
    {
        using var source = new WriteOnlyStream();
        source.Write(new BinarySerializer().Serialize(Sample()));
        source.Position = 0;

        Assert.Throws<NotSupportedException>(() => new BinarySerializer().Deserialize<Person>(source));
    }

    [Fact]
    public void Serialize_IntoAReadOnlyDestination_ThrowsNotSupported()
    {
        using var destination = new MemoryStream(new byte[256], writable: false);

        Assert.Throws<NotSupportedException>(
            () => new BinarySerializer().Serialize(destination, Sample()));
    }

    [Fact]
    public void Serialize_IntoANullDestination_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BinarySerializer().Serialize(null!, Sample()));
    }

    // --- STR-28: inspection restores the position, failure or not -------------------------------

    [Fact]
    public void Peek_OverAValidHeader_RestoresThePosition()
    {
        using var source = new MemoryStream();
        source.Write(new byte[16]);
        source.Write(new BinarySerializer().Serialize(Sample()));
        source.Position = 16;

        Assert.NotNull(BinaryFormatInspector.Peek(source));

        Assert.Equal(16, source.Position);
    }

    [Fact]
    public void Peek_OverAMalformedHeader_RestoresThePosition()
    {
        byte[] frame = new BinarySerializer().Serialize(Sample());
        using var source = new MemoryStream(
            Mutate.SetInt32(frame, Wire.UncompressedLengthOffset, -1), writable: false);

        Assert.Throws<BinaryFormatException>(() => BinaryFormatInspector.Peek(source));

        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void Peek_OverAnUnsupportedVersion_RestoresThePosition()
    {
        byte[] frame = Wire.FrameWith([], version: 99);
        using var source = new MemoryStream(frame, writable: false);

        Assert.Throws<BinaryFormatNotSupportedException>(() => BinaryFormatInspector.Peek(source));

        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void Peek_OverAHeaderlessPayload_ReturnsNullAndRestoresThePosition()
    {
        using var source = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8], writable: false);

        Assert.Null(BinaryFormatInspector.Peek(source));

        Assert.Equal(0, source.Position);
    }

    [Fact]
    public void Peek_OverASourceShorterThanTheMagic_ReturnsNullAndRestoresThePosition()
    {
        using var source = new MemoryStream([1, 2], writable: false);

        Assert.Null(BinaryFormatInspector.Peek(source));

        Assert.Equal(0, source.Position);
    }
}
