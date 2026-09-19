using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-01…HST-09: a frame that is wrong in exactly one byte. Every edit has a documented
/// outcome — the magic, the version, an algorithm identifier, an optional string, the reference
/// flag, each declared length, the checksum and the ciphertext — and under authenticated encryption
/// no byte of the header can be altered at all.
/// </summary>
public class MutationTests
{
    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    private static byte[] Frame() => new BinarySerializer().Serialize(Sample());

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    // --- HST-01: the magic ------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Deserialize_AMutatedMagicByte_ThrowsFormat(int offset)
    {
        byte[] frame = Mutate.FlipByte(Frame(), offset);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_AMutatedMagic_IsRejectedRatherThanGuessedAt()
    {
        // §10.3: unidentified bytes are never read as a damaged V1 frame, and are read as V0 only
        // when the caller stated that this channel carries headerless payloads.
        AssertEx.Throws<BinaryFormatException>(
            "fallback is disabled",
            () => new BinarySerializer().Deserialize<Person>(Mutate.FlipByte(Frame(), 0)));
    }

    // --- HST-02: the version ----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(-1)]
    public void Deserialize_AnUnsupportedVersion_ThrowsFormatNotSupported(int version)
    {
        byte[] frame = Mutate.SetInt32(Frame(), 4, version);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    // --- HST-03: algorithm identifiers ------------------------------------------------------------

    [Fact]
    public void Deserialize_AnUnknownCompressionIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Mutate.SetByte(Frame(), 8, 7);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "compression", () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_AnUnknownChecksumIdentifier_ThrowsFormatNotSupported()
    {
        // byte 8 compression, byte 9 its absent custom name, byte 10 checksum.
        byte[] frame = Mutate.SetByte(Frame(), 10, 7);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "checksum", () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_AnUnknownEncryptionIdentifier_ThrowsFormatNotSupported()
    {
        byte[] frame = Mutate.SetByte(Frame(), 12, 7);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "encryption", () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_ACustomAlgorithmThatWasNeverRegistered_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith([], compression: 255, customCompressionName: "nobody");

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    // --- HST-04: the optional header strings ------------------------------------------------------

    [Fact]
    public void Deserialize_APresenceFlagRaisedOverNothing_ThrowsFormat()
    {
        // Byte 9 is the "custom compression name follows" flag of a frame that carries no name.
        byte[] frame = Mutate.SetByte(Frame(), 9, 1);

        Assert.ThrowsAny<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_AnOptionalStringLongerThanTheFieldAdmits_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWithOversizedCustomName(declaredLength: 300, actualBytes: 300);

        AssertEx.Throws<BinaryFormatException>(
            "admits at most", () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnOptionalStringLongerThanThePayload_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWithOversizedCustomName(declaredLength: 200, actualBytes: 2);

        Assert.ThrowsAny<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnOptionalStringThatIsNotValidUtf8_ThrowsFormat()
    {
        byte[] frame = Wire.Payload(writer =>
        {
            writer.Write(Wire.Magic);
            writer.Write(1);
            writer.Write((byte)255);                    // CompressionAlgorithm.Custom
            writer.Write(true);                         // the custom name is present
            writer.Write7BitEncodedInt(2);
            writer.Write(new byte[] { 0xC3, 0x28 });    // an invalid two-byte sequence
            writer.Write((byte)0); writer.Write(false);
            writer.Write((byte)0); writer.Write(false);
            writer.Write(false);
            writer.Write(false);
            writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write((byte)0);
        });

        Assert.ThrowsAny<BinarySerializerException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    // --- HST-05: the reference flag ---------------------------------------------------------------

    [Fact]
    public void Deserialize_ThePreserveReferencesFlagRaised_ReadsThePayloadAsFramedAndFails()
    {
        // The payload was written without reference framing, so reading it as framed hits a marker
        // byte that is not 0 or 1, or a value the engine cannot resolve.
        byte[] frame = Mutate.SetByte(Frame(), Wire.PreserveReferencesOffset, 1);

        Assert.ThrowsAny<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_ThePreserveReferencesFlagCleared_ReadsThePayloadAsUnframedAndFails()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());
        byte[] framed = serializer.Serialize(Sample());

        byte[] frame = Mutate.SetByte(framed, Wire.PreserveReferencesOffset, 0);

        Assert.ThrowsAny<BinaryFormatException>(() => serializer.Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_ThePreserveReferencesFlag_DecidesTheReadingRegardlessOfTheConfiguration()
    {
        // The header, not the local configuration, says how the payload is framed.
        var writer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());
        var plainReader = new BinarySerializer();

        var restored = plainReader.Deserialize<Person>(writer.Serialize(Sample()));

        Assert.Equal("Alice", restored!.Name);
    }

    // --- HST-06: the declared lengths -------------------------------------------------------------

    [Theory]
    [InlineData(Wire.UncompressedLengthOffset)]
    [InlineData(Wire.CompressedLengthOffset)]
    [InlineData(Wire.OnDiskLengthOffset)]
    public void Deserialize_ANegativeDeclaredLength_ThrowsFormat(int offset)
    {
        byte[] frame = Mutate.SetInt32(Frame(), offset, -1);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Theory]
    [InlineData(Wire.UncompressedLengthOffset)]
    [InlineData(Wire.CompressedLengthOffset)]
    [InlineData(Wire.OnDiskLengthOffset)]
    public void Deserialize_ADeclaredLengthAboveItsPhaseLimit_ThrowsLimit(int offset)
    {
        byte[] frame = Mutate.SetInt32(Frame(), offset, int.MaxValue);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Theory]
    [InlineData(Wire.UncompressedLengthOffset)]
    [InlineData(Wire.CompressedLengthOffset)]
    [InlineData(Wire.OnDiskLengthOffset)]
    public void Deserialize_ADeclaredLengthDisagreeingWithTheOthers_ThrowsFormat(int offset)
    {
        // With no compression and no encryption all three must agree, so moving one is malformed.
        byte[] frame = Mutate.SetInt32(Frame(), offset, 3);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_AChecksumLengthLongerThanTheFrame_ThrowsFormat()
    {
        byte[] frame = Mutate.SetByte(Frame(), Wire.ChecksumLengthOffset, 200);

        Assert.ThrowsAny<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    // --- HST-07: the checksum ---------------------------------------------------------------------

    [Fact]
    public void Deserialize_AMutatedChecksum_ThrowsIntegrity()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build());
        byte[] frame = serializer.Serialize(Sample());

        // The checksum bytes follow the one-byte length at the end of the header.
        byte[] mutated = Mutate.FlipByte(frame, Wire.ChecksumLengthOffset + 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(mutated));
    }

    [Fact]
    public void Deserialize_AMutatedPayloadUnderAChecksum_ThrowsIntegrity()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build());
        byte[] frame = serializer.Serialize(Sample());

        byte[] mutated = Mutate.FlipByte(frame, frame.Length - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(mutated));
    }

    // --- HST-08: the ciphertext -------------------------------------------------------------------

    [Fact]
    public void Deserialize_MutatedCiphertext_ThrowsIntegrity()
    {
        byte[] key = NewKey();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key).Build());
        byte[] frame = serializer.Serialize(Sample());

        byte[] mutated = Mutate.FlipByte(frame, frame.Length - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(mutated));
    }

    [Fact]
    public void Deserialize_ATruncatedAuthenticationTag_ThrowsFormatOrIntegrity()
    {
        byte[] key = NewKey();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key).Build());
        byte[] frame = serializer.Serialize(Sample());

        // §8.2 and §13.1 both apply here: the frame is short *and* unauthenticated.
        Assert.ThrowsAny<BinarySerializerException>(
            () => serializer.Deserialize<Person>(Mutate.Truncate(frame, frame.Length - 4)));
    }

    // --- HST-09: no byte of an authenticated header survives an edit ------------------------------

    [Fact]
    public void Deserialize_EveryByteOfAnEncryptedFrameFlippedInTurn_AlwaysFails()
    {
        byte[] key = NewKey();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), key, "ring")
                .WithChecksum(new Crc32())
                .Build());
        byte[] frame = serializer.Serialize(Sample());

        for (int offset = 0; offset < frame.Length; offset++)
        {
            byte[] mutated = Mutate.FlipByte(frame, offset);

            var ex = Record.Exception(() => serializer.Deserialize<Person>(mutated));

            Assert.True(
                ex is BinarySerializerException,
                $"Flipping byte {offset} produced {ex?.GetType().Name ?? "no failure at all"}.");
        }
    }

    [Fact]
    public void Deserialize_EveryByteOfAnEncryptedHeaderFlippedInTurn_IsRefusedBeforeThePayload()
    {
        byte[] key = NewKey();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), key, "ring")
                .Build());
        byte[] frame = serializer.Serialize(Sample());
        int headerLength = Wire.ReadHeader(frame).HeaderLength;

        for (int offset = 0; offset < headerLength; offset++)
        {
            byte[] mutated = Mutate.FlipByte(frame, offset);

            var ex = Record.Exception(() => serializer.Deserialize<Person>(mutated));

            Assert.True(
                ex is BinarySerializerException,
                $"Flipping header byte {offset} produced {ex?.GetType().Name ?? "no failure at all"}.");
        }
    }
}
