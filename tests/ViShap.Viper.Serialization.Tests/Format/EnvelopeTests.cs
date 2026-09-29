using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins ENV-01, ENV-02, ENV-04…ENV-07 and ENV-10: the V1 envelope runs its phases in the order
/// §22.6 fixes, each phase verifies the size the header declared, and the wire meter counts the
/// bytes of one operation rather than the position of the caller's stream.
/// </summary>
public class EnvelopeTests
{
    /// <summary>A payload that deflates to well under its own size, so the phases stay separable.</summary>
    private static readonly string Compressible = new('a', 4096);

    private static readonly byte[] Key = new byte[32];

    // --- ENV-01: serialize, checksum the raw payload, compress, header, encrypt under it ---------

    [Fact]
    public void Serialize_CompressedFrame_ChecksumsTheRawPayloadRatherThanTheCompressedOne()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new DeflateCompression())
                .WithChecksum(new Sum8())
                .Build());

        var header = Wire.ReadHeader(serializer.Serialize(Compressible));

        // §22.7: the V0 payload is the same bytes the V1 frame carries before compression.
        byte[] rawPayload = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build()).Serialize(Compressible);

        byte[] expected = new byte[1];
        new Sum8().Compute(rawPayload, expected);

        Assert.Equal(expected, header.Checksum);
        Assert.Equal(rawPayload.Length, header.UncompressedLength);
        Assert.True(header.OnDiskLength < header.UncompressedLength);
    }

    [Fact]
    public void Serialize_CompressedAndEncryptedFrame_EncryptsWhatCompressionProduced()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new DeflateCompression())
                .WithEncryption(new Aes256GcmEncryption(), Key)
                .Build());

        var header = Wire.ReadHeader(serializer.Serialize(Compressible));
        byte[] rawPayload = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build()).Serialize(Compressible);
        int compressedLength = Compress(rawPayload).Length;

        Assert.True(compressedLength < header.UncompressedLength);
        Assert.Equal(new Aes256GcmEncryption().GetCiphertextLength(compressedLength), header.OnDiskLength);
    }

    [Fact]
    public void Serialize_V1Frame_WritesTheHeaderAheadOfTheDeclaredPayload()
    {
        byte[] frame = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), Key).Build())
            .Serialize(Compressible);

        var header = Wire.ReadHeader(frame);

        Assert.Equal(Wire.Magic, BitConverter.ToInt32(frame));
        Assert.Equal(header.HeaderLength + header.OnDiskLength, frame.Length);
    }

    // --- ENV-02: reading reverses the order -----------------------------------------------------

    [Fact]
    public void Deserialize_ChecksumComputedOverTheCompressedBytes_IsRejectedAfterDecompression()
    {
        // A checksum that matches the compressed bytes instead of the raw payload proves which of
        // the two the reader verifies, and therefore that it decompresses first.
        byte[] rawPayload = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build()).Serialize(Compressible);

        byte[] compressed = Compress(rawPayload);
        byte[] wrongChecksum = new byte[4];
        new Crc32Checksum().Compute(compressed, wrongChecksum);

        byte[] frame = Wire.FrameWith(
            compressed,
            services:
            [
                Wire.ChecksumRecord((byte)ChecksumAlgorithm.Crc32, wrongChecksum),
                Wire.CompressionRecord((byte)CompressionAlgorithm.Deflate, rawPayload.Length)
            ]);

        Assert.Throws<BinaryIntegrityException>(() => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_TamperedCiphertext_FailsInDecryptionBeforeDecompression()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithCompression(new DeflateCompression())
                .WithEncryption(new Aes256GcmEncryption(), Key)
                .Build());

        byte[] frame = serializer.Serialize(Compressible);
        var header = Wire.ReadHeader(frame);

        // Malformed deflate input would be a format error; an integrity error shows decryption ran.
        byte[] tampered = Mutate.FlipByte(frame, header.HeaderLength + 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    // --- ENV-04…ENV-08: every declared length is verified against what arrives ------------------

    [Fact]
    public void Deserialize_PayloadShorterThanTheRootValueDemands_ThrowsFormat()
    {
        // A string declaring five UTF-8 bytes in a payload that ends after the length prefix.
        byte[] frame = Wire.Frame([1, 5]);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_BytesAfterTheDeclaredPayload_AreNeitherConsumedNorAnError()
    {
        byte[] frame = new BinarySerializer().Serialize(42);

        using var stream = new MemoryStream();
        stream.Write(frame);
        stream.Write([9, 9, 9, 9]);
        stream.Position = 0;

        Assert.Equal(42, new BinarySerializer().Deserialize<int>(stream));
        Assert.Equal(frame.Length, stream.Position);
    }

    [Fact]
    public void Deserialize_DecompressedPayloadShorterThanDeclared_ThrowsFormat()
    {
        byte[] frame = CompressedFrame(out int rawLength);
        frame = Wire.WithLengths(frame, uncompressedLength: rawLength + 1);

        AssertEx.Throws<BinaryFormatException>(
            $"expected {rawLength + 1}",
            () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_DecompressedPayloadLongerThanDeclared_ThrowsFormat()
    {
        byte[] frame = CompressedFrame(out int rawLength);
        frame = Wire.WithLengths(frame, uncompressedLength: rawLength - 1);

        AssertEx.Throws<BinaryFormatException>(
            "more data than the declared uncompressed length",
            () => new BinarySerializer().Deserialize<string>(frame));
    }

    // --- ENV-10: the wire meter counts this operation, not the stream's position -----------------

    [Fact]
    public void Deserialize_FrameAtANonZeroOffset_ChargesTheWireBudgetFromZero()
    {
        byte[] frame = new BinarySerializer().Serialize(42);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxWireBytes = frame.Length })
                .Build());

        using var stream = new MemoryStream();
        stream.Write(new byte[4096]);
        stream.Write(frame);
        stream.Position = 4096;

        Assert.Equal(42, serializer.Deserialize<int>(stream));
    }

    [Fact]
    public void Deserialize_FrameLongerThanTheWireBudget_ThrowsLimitWhateverTheOffset()
    {
        byte[] frame = new BinarySerializer().Serialize(42);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxWireBytes = frame.Length - 1 })
                .Build());

        using var stream = new MemoryStream();
        stream.Write(new byte[4096]);
        stream.Write(frame);
        stream.Position = 4096;

        AssertEx.Throws<BinaryLimitException>("wire", () => serializer.Deserialize<int>(stream));
    }

    private static byte[] Compress(byte[] rawPayload)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        new DeflateCompression().Compress(rawPayload, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A hand-built Deflate frame whose header tells the truth about every phase.</summary>
    private static byte[] CompressedFrame(out int rawLength)
    {
        byte[] rawPayload = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build()).Serialize(Compressible);

        byte[] compressed = Compress(rawPayload);
        rawLength = rawPayload.Length;

        return Wire.FrameWith(
            compressed,
            services: [Wire.CompressionRecord((byte)CompressionAlgorithm.Deflate, rawPayload.Length)]);
    }
}
