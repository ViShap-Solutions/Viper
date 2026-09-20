using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Metadata;

/// <summary>
/// Pins INS-01…INS-10: inspection reads the envelope, leaves the stream where it found it,
/// separates "not a payload I recognize" from "a payload that is malformed", applies the limits it
/// was handed, and reports what the header says and nothing else.
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

    [Fact]
    public void Peek_RecognizedMagicWithAnUnsupportedVersion_ThrowsFormatNotSupported()
    {
        // The magic identifies the family, so an unknown version is a version this build cannot
        // read, not unrelated data: null would invite the caller to treat it as someone else's.
        using var stream = new MemoryStream(Wire.FrameWith(Wire.Payload(writer => writer.Write(1)), version: 2));

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "2", () => BinaryFormatInspector.Peek(stream));
    }

    [Fact]
    public void Peek_MalformedHeader_RestoresTheStartingPosition()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[16]);
        stream.Write(Mutate.Truncate(_serializer.Serialize(123), 12));
        stream.Position = 16;

        Assert.Throws<BinaryFormatException>(() => BinaryFormatInspector.Peek(stream));

        Assert.Equal(16, stream.Position);
    }

    [Fact]
    public void Peek_UnsupportedVersion_RestoresTheStartingPosition()
    {
        using var stream = new MemoryStream(Wire.FrameWith(Wire.Payload(writer => writer.Write(1)), version: 2));
        stream.Position = 0;

        Assert.Throws<BinaryFormatNotSupportedException>(() => BinaryFormatInspector.Peek(stream));

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Peek_DeclaredPayloadWithinTheSuppliedLimits_Succeeds()
    {
        using var stream = new MemoryStream(DeclaringPayloadBytes(4096));

        var info = BinaryFormatInspector.Peek(stream, SerializationLimits.Default with { MaxPayloadBytes = 4096 });

        Assert.Equal(1, info!.Value.FormatVersion);
    }

    [Fact]
    public void Peek_DeclaredPayloadBeyondTheSuppliedLimits_ThrowsLimit()
    {
        // The same bytes the previous test accepts: what changed is the policy handed to Peek.
        using var stream = new MemoryStream(DeclaringPayloadBytes(4096));

        AssertEx.Throws<BinaryLimitException>(
            "UncompressedLength",
            () => BinaryFormatInspector.Peek(stream, SerializationLimits.Default with { MaxPayloadBytes = 4095 }));
    }

    [Fact]
    public void Peek_WireBudgetSmallerThanTheHeader_ThrowsLimit()
    {
        using var stream = new MemoryStream(_serializer.Serialize(123));

        Assert.Throws<BinaryLimitException>(
            () => BinaryFormatInspector.Peek(stream, SerializationLimits.Default with { MaxWireBytes = 8 }));
    }

    [Fact]
    public void Peek_StreamFailsBeforeTheMagic_ThrowsStream()
    {
        using var stream = new FailingStream(bytesBeforeFailure: 4);

        AssertEx.Throws<BinaryStreamException>(
            "inspect", () => BinaryFormatInspector.Peek(stream));
    }

    [Fact]
    public void Peek_StreamFailsWhileTheHeaderIsRead_ThrowsStream()
    {
        // Past the magic, so the failure happens inside the header read rather than in the probe.
        using var stream = new FailingContentStream(_serializer.Serialize(123), bytesBeforeFailure: 12);

        AssertEx.Throws<BinaryStreamException>(
            "inspect", () => BinaryFormatInspector.Peek(stream));
    }

    [Fact]
    public void Peek_CustomAlgorithms_ReportsTheRegisteredNamesTheHeaderCarries()
    {
        var options = BinarySerializerOptions.Configure()
            .WithCompression(new IdentityCompression())
            .WithChecksum(new Sum8())
            .WithEncryption(new UnauthenticatedCipher(), Key, keyId: "primary")
            .Build();

        using var stream = new MemoryStream(new BinarySerializer(options).Serialize(new Person { Name = "Alice", Age = 30 }));
        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(CompressionAlgorithm.Custom, info.Compression);
        Assert.Equal(IdentityCompression.RegisteredName, info.CustomCompressionName);
        Assert.Equal(ChecksumAlgorithm.Custom, info.ChecksumAlgorithm);
        Assert.Equal(Sum8.RegisteredName, info.CustomChecksumName);
        Assert.Equal(EncryptionAlgorithm.Custom, info.Encryption);
        Assert.Equal(UnauthenticatedCipher.RegisteredName, info.CustomEncryptionName);
        Assert.Equal("primary", info.KeyId);
    }

    [Fact]
    public void Peek_EncryptedPayload_ReportsNoKeyMaterial()
    {
        var options = BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), Key, keyId: "primary")
            .Build();

        using var stream = new MemoryStream(new BinarySerializer(options).Serialize(123));
        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal("primary", info.KeyId);
        AssertEx.DoesNotContainBytes(stream.ToArray(), Key);
    }

    /// <summary>The 32 bytes AES-256 needs; the value itself is irrelevant to what is asserted.</summary>
    private static readonly byte[] Key =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    /// <summary>A header declaring <paramref name="length"/> for all three phases, over a body that short.</summary>
    private static byte[] DeclaringPayloadBytes(int length) =>
        Wire.Frame(Wire.Payload(writer => writer.Write(new byte[length])));
}
