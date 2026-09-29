using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-02…HDR-08, HDR-12, HDR-16, HDR-18, HDR-19 and HDR-21…HDR-33: the V1 header is a list of
/// service records with exactly one encoding, it owns every invariant over the values it declares,
/// and it decides them before a byte of payload is touched.
/// </summary>
public class HeaderTests
{
    /// <summary>An <c>int32</c> root of 42: no null, four payload bytes.</summary>
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
        for (int length = 1; length < Wire.Header(Int42.Length).Length; length++)
            lengths.Add(length);

        return lengths;
    }

    [Fact]
    public void Deserialize_HeaderTruncatedInsideAServiceRecord_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.CompressionRecord(1, 4)]);

        foreach (byte[] prefix in Mutate.Prefixes(frame[..Wire.ReadHeader(frame).HeaderLength]))
            Assert.Throws<BinaryFormatException>(() => Default.Deserialize<int>(prefix));
    }

    [Fact]
    public void Deserialize_UndefinedCompressionIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.CompressionRecord(3, 4)]);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "compression", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_UndefinedChecksumIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.ChecksumRecord(200, [0, 0, 0, 0])]);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "checksum", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_UndefinedEncryptionIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.EncryptionRecord(200)]);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "encryption", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnIdentifierBeyondAByte_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.Service(Wire.CompressionService, critical: true, [.. Wire.Varint(256), 4])]);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "compression", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Peek_NoService_ReportsNoAlgorithmAndNoName()
    {
        using var stream = new MemoryStream(Default.Serialize(42));

        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(CompressionAlgorithm.None, info.Compression);
        Assert.Equal(ViShap.Viper.Checksum.ChecksumAlgorithm.None, info.ChecksumAlgorithm);
        Assert.Equal(EncryptionAlgorithm.None, info.Encryption);
        Assert.Null(info.CustomCompressionName);
        Assert.Null(info.CustomChecksumName);
        Assert.Null(info.CustomEncryptionName);
        Assert.Null(info.KeyId);
        Assert.Null(info.UncompressedLength);
        Assert.True(info.Checksum.IsEmpty);
    }

    [Fact]
    public void Peek_CustomNamesAndKeyIdPopulated_ReadBackEveryName()
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

    [Theory]
    [InlineData(new byte[] { 0x00 }, null)]
    [InlineData(new byte[] { 0x01 }, "")]
    [InlineData(new byte[] { 0x03, (byte)'k', (byte)'7' }, "k7")]
    public void Peek_KeyIdAbsentEmptyOrPopulated_ReadsBackAsWritten(byte[] keyIdBytes, string? expected)
    {
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.Service(Wire.EncryptionService, critical: true, [1, .. keyIdBytes])]);

        var info = BinaryFormatInspector.Peek(frame)!.Value;

        Assert.Equal(expected, info.KeyId);
    }

    [Theory]
    [InlineData(nameof(SerializationLimits.MaxEncryptedBytes), false)]
    [InlineData(nameof(SerializationLimits.MaxPayloadBytes), false)]
    [InlineData(nameof(SerializationLimits.MaxCompressedBytes), true)]
    public void Deserialize_OnDiskLengthAboveItsPhaseLimit_ThrowsLimitBeforeAllocating(string tightened, bool compressed)
    {
        // Only the limit named is tightened, so the failure names it alone. Without encryption the
        // stored bytes are the compressed payload when there is compression, and the payload itself
        // when there is not.
        var serializer = Tightened(tightened);

        byte[] frame = Wire.FrameWith(
            Int42,
            services: compressed ? [Wire.CompressionRecord(1, 4)] : [],
            onDiskLength: 1 << 28);

        AssertEx.Throws<BinaryLimitException>(
            $"exceeds the configured maximum of 16 ({tightened})", () => serializer.Deserialize<int>(frame));

        AssertEx.AllocatesLessThan(1 << 20, () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_UncompressedLengthAbovePayloadLimit_ThrowsLimitBeforeAllocating()
    {
        var serializer = Tightened(nameof(SerializationLimits.MaxPayloadBytes));

        byte[] frame = Wire.FrameWith([1, 2, 3, 4], services: [Wire.CompressionRecord(1, 1 << 28)]);

        AssertEx.Throws<BinaryLimitException>(
            "exceeds the configured maximum of 16 (MaxPayloadBytes)", () => serializer.Deserialize<int>(frame));

        AssertEx.AllocatesLessThan(1 << 20, () => serializer.Deserialize<int>(frame));
    }

    // --- HDR-33: the serializer writes the service records of the contract's examples byte for byte ---

    [Fact]
    public void Serialize_ChecksumAndCustomCompression_WritesTheDocumentedRecords()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithChecksum(new Crc32Checksum())
            .WithCompression(new IdentityCompression("lz4x"))
            .RegisterCustomCompression("lz4x", () => new IdentityCompression("lz4x"))
            .Build());

        // A byte[] of 998 zeros is a 1 000-byte payload: its count plus one, 999, in two bytes.
        byte[] payload = Wire.Payload(writer => { writer.Write7BitEncodedInt(999); writer.Write(new byte[998]); });
        Assert.Equal(1000, payload.Length);

        byte[] hash = new byte[4];
        new Crc32Checksum().Compute(payload, hash);

        byte[] frame = serializer.Serialize(new byte[998]);

        byte[] expected =
        [
            0x02,                                                          // two service records
            0x03, 0x05, 0x01, .. hash,                                     // CRC-32 (id 1), 4-byte hash
            0x05, 0x09, 0xFF, 0x01, 0x04, 0x6C, 0x7A, 0x34, 0x78, 0xE8, 0x07 // "lz4x", 1 000 bytes
        ];
        Assert.Equal(expected, frame[6..(6 + expected.Length)]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(byte.MaxValue)]
    public void Serialize_ChecksumOfAnyRepresentableWidth_IsTheRemainderOfItsRecordAndRoundTrips(int width)
    {
        var serializer = Wide(width);

        byte[] frame = serializer.Serialize(42);
        var header = Wire.ReadHeader(frame);
        var record = header.Service(Wire.ChecksumService);

        Assert.Equal(width, header.Checksum.Length);
        Assert.Equal(record.BodyOffset + record.BodyLength, header.OnDiskLengthOffset);
        Assert.Equal(42, serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_NoChecksum_WritesNoChecksumRecord()
    {
        var header = Wire.ReadHeader(Default.Serialize(42));

        Assert.Empty(header.Services);
        Assert.Empty(header.Checksum);
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

        // The reader is configured without the option; the payload mode is what decides.
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

    // --- service records ---------------------------------------------------------------------------

    [Fact]
    public void Deserialize_ServicesOutOfOrder_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.EncryptionRecord(1), Wire.CompressionRecord(1, 4)]);

        AssertEx.Throws<BinaryFormatException>(
            "ascending number", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ARepeatedServiceNumber_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.CompressionRecord(1, 4), Wire.CompressionRecord(2, 4)]);

        AssertEx.Throws<BinaryFormatException>(
            "more than once", () => Default.Deserialize<int>(frame));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialize_ServiceNumberZero_ThrowsFormat(bool critical)
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(0, critical, [])]);

        AssertEx.Throws<BinaryFormatException>(
            "Service number 0 is reserved", () => Default.Deserialize<int>(frame));
    }

    [Theory]
    [InlineData(Wire.ChecksumService)]
    [InlineData(Wire.CompressionService)]
    [InlineData(Wire.EncryptionService)]
    public void Deserialize_AKnownServiceMarkedSkippable_ThrowsFormat(int number)
    {
        byte[] body = number switch
        {
            Wire.ChecksumService => [1, 0, 0, 0, 0],
            Wire.CompressionService => [1, 4],
            _ => [1, 0]
        };

        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(number, critical: false, body)]);

        AssertEx.Throws<BinaryFormatException>(
            "may be skipped", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnUnknownCriticalService_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(5, critical: true, [1, 2, 3])]);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "Service 5 is critical", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnUnknownSkippableService_IsSkippedByItsLength()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(5, critical: false, [0xFF, 0x00, 0x7F])]);

        Assert.Equal(42, Default.Deserialize<int>(frame));
        Assert.NotNull(BinaryFormatInspector.Peek(frame));
    }

    [Fact]
    public void Deserialize_AServiceBodyWithBytesAfterItsLastField_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.Service(Wire.CompressionService, critical: true, [1, 4, 0])]);

        AssertEx.Throws<BinaryFormatException>(
            "after its last field", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AServiceBodyShorterThanItsFields_ThrowsFormat()
    {
        // The uncompressed length would be read past the body the record declared.
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.Service(Wire.CompressionService, critical: true, [1, 4], declaredLength: 1)]);

        Assert.Throws<BinaryFormatException>(() => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AHeaderLongerThanItsBound_ThrowsFormatBeforeTheBodyIsRead()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(5, critical: false, new byte[4096])]);

        AssertEx.Throws<BinaryFormatException>(
            "4096-byte header bound", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AHeaderThatFillsItsBoundExactly_IsRead()
    {
        // 4 magic + 1 version + 1 mode + 1 count + record (kind 1, length 2, body) + onDiskLength 1.
        int body = BinaryFormatHeaderV1.MaxLength - 4 - 1 - 1 - 1 - 1 - 2 - 1;
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(5, critical: false, new byte[body])]);

        Assert.Equal(BinaryFormatHeaderV1.MaxLength, Wire.ReadHeader(frame).HeaderLength);
        Assert.Equal(42, Default.Deserialize<int>(frame));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0x80)]
    public void Deserialize_AReservedPayloadModeBit_ThrowsFormatNotSupported(int mode)
    {
        byte[] frame = Wire.FrameWith(Int42, mode: mode);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "Payload mode", () => Default.Deserialize<int>(frame));
    }

    [Theory]
    [InlineData(Wire.ChecksumService)]
    [InlineData(Wire.CompressionService)]
    [InlineData(Wire.EncryptionService)]
    public void Deserialize_ARecordNamingIdNone_ThrowsFormat(int number)
    {
        byte[] body = number switch
        {
            Wire.ChecksumService => [0, 0, 0, 0, 0],
            Wire.CompressionService => [0, 4],
            _ => [0, 0]
        };

        byte[] frame = Wire.FrameWith(Int42, services: [Wire.Service(number, critical: true, body)]);

        AssertEx.Throws<BinaryFormatException>(
            "names no algorithm", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ACustomAlgorithmWithAnEmptyName_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(
            Int42, services: [Wire.Service(Wire.CompressionService, critical: true, [0xFF, 0x01, 0x00, 4])]);

        AssertEx.Throws<BinaryFormatException>(
            "empty name", () => Default.Deserialize<int>(frame));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Deserialize_AChecksumOfTheWrongLengthForItsAlgorithm_ThrowsFormat(int length)
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.ChecksumRecord(1, new byte[length])]);

        AssertEx.Throws<BinaryFormatException>(
            "Checksum length", () => Default.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AChecksumRecordWithoutAHash_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(Int42, services: [Wire.ChecksumRecord(1, [])]);

        AssertEx.Throws<BinaryFormatException>(
            "between 1 and 255", () => Default.Deserialize<int>(frame));
    }

    private static BinarySerializer Tightened(string tightened)
    {
        var limits = SerializationLimits.Default with
        {
            MaxPayloadBytes = tightened == nameof(SerializationLimits.MaxPayloadBytes) ? 16 : 1 << 29,
            MaxCompressedBytes = tightened == nameof(SerializationLimits.MaxCompressedBytes) ? 16 : 1 << 29,
            MaxEncryptedBytes = tightened == nameof(SerializationLimits.MaxEncryptedBytes) ? 16 : 1 << 29,
            MaxWireBytes = 1 << 29
        };

        return new BinarySerializer(BinarySerializerOptions.Configure().WithLimits(limits).Build());
    }

    private static BinarySerializer Wide(int checksumBytes) =>
        new(BinarySerializerOptions.Configure()
            .WithChecksum(new WideChecksum(checksumBytes))
            .RegisterCustomChecksum(WideChecksum.RegisteredName, () => new WideChecksum(checksumBytes))
            .Build());
}
