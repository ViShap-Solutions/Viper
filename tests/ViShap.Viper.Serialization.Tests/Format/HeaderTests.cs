using System.Text;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-02…HDR-08, HDR-11…HDR-14, HDR-16, HDR-18 and HDR-19: the V1 header owns the field order
/// of §22.6 and every invariant over the values it declares, and it decides them before a byte of
/// payload is touched.
/// </summary>
public class HeaderTests
{
    /// <summary>An <c>int32</c> root of 42: no null flag, four payload bytes.</summary>
    private static readonly byte[] Int42 = [42, 0, 0, 0];

    private static BinarySerializer Default => new();

    [Fact]
    public void Deserialize_MagicMismatch_ThrowsFormat()
    {
        byte[] frame = Mutate.SetInt32(Wire.Frame(Int42), 0, Wire.Magic ^ 0x01);

        AssertEx.Throws<BinaryFormatException>(
            "magic number mismatch", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_UnknownVersion_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, version: 2);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "2", () => Default.Deserialize<int>(frame));
    }

    [Theory]
    [MemberData(nameof(HeaderPrefixLengths))]
    public void Deserialize_HeaderTruncatedAtAnyPrefix_ThrowsFormat(int length)
    {
        byte[] frame = Mutate.Truncate(Wire.Frame(Int42), length);

        Assert.Throws<BinaryFormatException>(() => Default.Deserialize<int>(frame));
    }

    public static TheoryData<int> HeaderPrefixLengths()
    {
        var lengths = new TheoryData<int>();
        for (int length = 1; length < Wire.PlainHeaderLength; length++)
            lengths.Add(length);

        return lengths;
    }

    [Fact]
    public void Deserialize_UndefinedCompressionIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, compression: 3);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "compression", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_UndefinedChecksumIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, checksumAlgorithm: 2);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "checksum", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_UndefinedEncryptionIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, encryption: 2);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "encryption", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Peek_OptionalStringsAbsent_ReadBackAsNull()
    {
        using var stream = new MemoryStream(Default.Serialize(42));

        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Null(info.CustomCompressionName);
        Assert.Null(info.CustomChecksumName);
        Assert.Null(info.CustomEncryptionName);
        Assert.Null(info.KeyId);
    }

    [Fact]
    public void Peek_OptionalStringsPopulated_ReadBackEveryName()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new IdentityCompression())
                .WithChecksum(new Sum8())
                .WithEncryption(new UnauthenticatedCipher(), new byte[32], keyId: "ring-7")
                .Build());

        using var stream = new MemoryStream(serializer.Serialize(42));
        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(IdentityCompression.RegisteredName, info.CustomCompressionName);
        Assert.Equal(Sum8.RegisteredName, info.CustomChecksumName);
        Assert.Equal(UnauthenticatedCipher.RegisteredName, info.CustomEncryptionName);
        Assert.Equal("ring-7", info.KeyId);
    }

    [Fact]
    public void Peek_OptionalStringDeclaredPresentButEmpty_ReadsBackAsEmpty()
    {
        // A present flag followed by a zero length is legal framing, and is not the same as absent.
        using var stream = new MemoryStream(Wire.FrameWith(Int42, keyId: string.Empty));

        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(string.Empty, info.KeyId);
    }

    [Theory]
    [InlineData(Wire.UncompressedLengthOffset)]
    [InlineData(Wire.CompressedLengthOffset)]
    [InlineData(Wire.OnDiskLengthOffset)]
    public void Deserialize_NegativeDeclaredLength_ThrowsFormat(int offset)
    {
        byte[] frame = Mutate.SetInt32(Wire.Frame(Int42), offset, -1);

        AssertEx.Throws<BinaryFormatException>(
            "must be non-negative", () => Default.Deserialize<int>(frame));
    }

    [Theory]
    [InlineData(Wire.UncompressedLengthOffset, nameof(SerializationLimits.MaxPayloadBytes))]
    [InlineData(Wire.CompressedLengthOffset, nameof(SerializationLimits.MaxCompressedBytes))]
    [InlineData(Wire.OnDiskLengthOffset, nameof(SerializationLimits.MaxEncryptedBytes))]
    public void Deserialize_DeclaredLengthAboveItsPhaseLimit_ThrowsLimitBeforeAllocating(
        int offset,
        string tightened)
    {
        // Only the phase this offset belongs to is tightened, so the failure names that phase alone.
        var limits = SerializationLimits.Default with
        {
            MaxPayloadBytes = tightened == nameof(SerializationLimits.MaxPayloadBytes) ? 16 : 1 << 20,
            MaxCompressedBytes = tightened == nameof(SerializationLimits.MaxCompressedBytes) ? 16 : 1 << 20,
            MaxEncryptedBytes = tightened == nameof(SerializationLimits.MaxEncryptedBytes) ? 16 : 1 << 20,
            MaxWireBytes = 1 << 20
        };

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithLimits(limits).Build());

        byte[] frame = Mutate.SetInt32(Wire.Frame(Int42), offset, 1 << 28);

        AssertEx.Throws<BinaryLimitException>(
            "exceeds the configured maximum of 16", () => serializer.Deserialize<int>(frame));

        AssertEx.AllocatesLessThan(1 << 20, () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_NoCompressionWithMismatchedCompressedLength_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(Int42, uncompressedLength: 4, compressedLength: 5, onDiskLength: 5);

        AssertEx.Throws<BinaryFormatException>(
            "CompressedLength must equal UncompressedLength",
            () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_NoEncryptionWithMismatchedOnDiskLength_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(Int42, uncompressedLength: 4, compressedLength: 4, onDiskLength: 5);

        AssertEx.Throws<BinaryFormatException>(
            "OnDiskLength must equal CompressedLength",
            () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_NoChecksum_DeclaresAChecksumLengthOfZero()
    {
        byte[] frame = Default.Serialize(42);

        Assert.Equal(0, frame[Wire.ChecksumLengthOffset]);
        Assert.Equal(42, Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_ChecksumAtTheWidestRepresentableLength_RoundTrips()
    {
        var serializer = Wide(byte.MaxValue);

        byte[] frame = serializer.Serialize(42);

        // The custom checksum name sits between the plain header and the checksum length: a 7-bit
        // length prefix plus its UTF-8 bytes.
        int checksumLengthOffset =
            Wire.ChecksumLengthOffset + 1 + Encoding.UTF8.GetByteCount(WideChecksum.RegisteredName);

        Assert.Equal(byte.MaxValue, frame[checksumLengthOffset]);
        Assert.Equal(checksumLengthOffset + 1 + byte.MaxValue + Int42.Length, frame.Length);
        Assert.Equal(42, serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_ChecksumWiderThanTheHeaderRepresentation_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "cannot be represented by the V1 header",
            () => Wide(byte.MaxValue + 1).Serialize(42));
    }

    [Fact]
    public void Deserialize_PreserveReferencesInTheHeader_DecidesThePayloadInterpretation()
    {
        var writer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());

        var shared = new List<int> { 1 };
        byte[] frame = writer.Serialize(new SharedLists { A = shared, B = shared });

        // The reader is configured without the option; the header is what decides.
        var restored = new BinarySerializer().Deserialize<SharedLists>(frame)!;

        Assert.Same(restored.A, restored.B);
    }

    [Fact]
    public void Deserialize_PayloadWithoutReferenceFraming_IsReadPlainlyByAPreservingReader()
    {
        byte[] frame = new BinarySerializer().Serialize(new SharedLists { A = [1], B = [2] });

        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());

        var restored = reader.Deserialize<SharedLists>(frame)!;

        Assert.Equal([1], restored.A);
        Assert.Equal([2], restored.B);
    }

    private static BinarySerializer Wide(int checksumBytes) =>
        new(BinarySerializerOptions.Configure()
            .WithChecksum(new WideChecksum(checksumBytes))
            .RegisterCustomChecksum(WideChecksum.RegisteredName, () => new WideChecksum(checksumBytes))
            .Build());
}
