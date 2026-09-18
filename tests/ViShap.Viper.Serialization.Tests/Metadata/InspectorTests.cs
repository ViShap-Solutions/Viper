using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Metadata;

/// <summary>
/// Pins INS-01…INS-04, INS-08 and INS-10: inspection reads the envelope, leaves the stream where it
/// found it, and separates "not a payload I recognize" from "a payload that is malformed".
/// </summary>
public class InspectorTests
{
    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Peek_ReadsTheHeaderWithoutConsumingTheStream()
    {
        using var stream = new MemoryStream();
        _serializer.Serialize(stream, new Person { Name = "Alice", Age = 30 });
        stream.Position = 0;

        var info = BinaryFormatInspector.Peek(stream);

        Assert.NotNull(info);
        Assert.Equal(1, info!.Value.FormatVersion);
        Assert.Equal(0, stream.Position);
        Assert.Equivalent(
            new Person { Name = "Alice", Age = 30 },
            _serializer.Deserialize<Person>(stream));
    }

    [Fact]
    public void Peek_RestoresANonZeroStartingPosition()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[16]);
        _serializer.Serialize(stream, 123);
        stream.Position = 16;

        BinaryFormatInspector.Peek(stream);

        Assert.Equal(16, stream.Position);
    }

    [Fact]
    public void Peek_ReportsTheAlgorithmsAndKeyIdOfTheEnvelope()
    {
        using var stream = new MemoryStream();
        _serializer.Serialize(stream, 123);
        stream.Position = 0;

        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(Compression.CompressionAlgorithm.None, info.Compression);
        Assert.Equal(Checksum.ChecksumAlgorithm.None, info.ChecksumAlgorithm);
        Assert.Equal(Crypto.EncryptionAlgorithm.None, info.Encryption);
        Assert.Null(info.KeyId);
    }

    [Fact]
    public void Peek_UnrecognizedBytes_ReturnsNull()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Null(BinaryFormatInspector.Peek(stream));
    }

    [Fact]
    public void Peek_RecognizedButMalformedHeader_ThrowsFormat()
    {
        // Null is reserved for "not a supported format"; a broken payload is a format error.
        using var stream = new MemoryStream(Mutate.Truncate(_serializer.Serialize(123), 12));

        Assert.Throws<BinaryFormatException>(() => BinaryFormatInspector.Peek(stream));
    }

    [Fact]
    public void Peek_NonSeekableStream_ThrowsNotSupported()
    {
        using var stream = new NonSeekableStream(_serializer.Serialize(123));

        Assert.Throws<NotSupportedException>(() => BinaryFormatInspector.Peek(stream));
    }

    [Fact]
    public void Peek_NullLimits_ThrowsArgumentNull()
    {
        using var stream = new MemoryStream(_serializer.Serialize(123));

        Assert.Throws<ArgumentNullException>(() => BinaryFormatInspector.Peek(stream, null!));
    }

    [Fact]
    public void Peek_InvalidLimits_ThrowsConfiguration()
    {
        using var stream = new MemoryStream(_serializer.Serialize(123));

        Assert.Throws<BinaryConfigurationException>(
            () => BinaryFormatInspector.Peek(stream, SerializationLimits.Default with { MaxDepth = 0 }));
    }
}
