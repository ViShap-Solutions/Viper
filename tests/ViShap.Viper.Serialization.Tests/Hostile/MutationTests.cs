using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-01…HST-09: a frame that is wrong in exactly one field. Every edit has a documented
/// outcome — the magic, the version, an algorithm identifier, a header string, the payload mode,
/// each declared length, the checksum and the ciphertext — and under authenticated encryption no
/// byte of the header, <c>onDiskLength</c> included, can be altered at all.
/// </summary>
public class MutationTests
{
    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    private static byte[] Frame() => new BinarySerializer().Serialize(Sample());

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static readonly byte[] FixedKey = new byte[32];

    /// <summary>A serializer that writes every service: CRC-32, Deflate and AES-256-GCM.</summary>
    private static BinarySerializer Protected(string? keyId = "ring") =>
        new(BinarySerializerOptions.Configure()
            .WithChecksum(new Crc32Checksum())
            .WithCompression(new ViShap.Viper.Compression.DeflateCompression())
            .WithEncryption(new Aes256GcmEncryption(), FixedKey, keyId)
            .Build());

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
    [InlineData(0x7F)]
    public void Deserialize_AnUnsupportedVersion_ThrowsFormatNotSupported(byte version)
    {
        byte[] frame = Mutate.SetByte(Frame(), 4, version);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    // --- HST-03: algorithm identifiers ------------------------------------------------------------

    [Theory]
    [InlineData(Wire.ChecksumService, "checksum")]
    [InlineData(Wire.CompressionService, "compression")]
    [InlineData(Wire.EncryptionService, "encryption")]
    public void Deserialize_AnUnknownAlgorithmIdentifier_ThrowsFormatNotSupported(int service, string what)
    {
        byte[] frame = Protected().Serialize(Sample());
        var record = Wire.ReadHeader(frame).Service(service);

        byte[] mutated = Mutate.SetByte(frame, record.BodyOffset, 7);

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            what, () => Protected().Deserialize<Person>(mutated));
    }

    [Fact]
    public void Deserialize_ACustomAlgorithmThatWasNeverRegistered_ThrowsFormatNotSupported()
    {
        byte[] frame = Wire.FrameWith([], services: [Wire.CompressionRecord(255, 0, "nobody")]);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    // --- HST-04: the header strings ---------------------------------------------------------------

    [Fact]
    public void Deserialize_AnAbsentKeyIdRaisedOverNothing_ThrowsFormat()
    {
        // The encryption record of a frame without a key id is the algorithm id, then zero. Raising
        // the zero declares key id bytes the record does not hold.
        byte[] frame = Protected(keyId: null).Serialize(Sample());
        var record = Wire.ReadHeader(frame).Service(Wire.EncryptionService);

        byte[] mutated = Mutate.SetByte(frame, record.BodyOffset + 1, 5);

        Assert.Throws<BinaryFormatException>(
            () => Protected(keyId: null).Deserialize<Person>(mutated));
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

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AHeaderStringThatIsNotValidUtf8_ThrowsFormat()
    {
        // A custom compression name of two bytes that are not a UTF-8 sequence.
        byte[] frame = Wire.FrameWith(
            [], services: [Wire.Service(Wire.CompressionService, critical: true, [0xFF, 0x01, 0x02, 0xC3, 0x28, 0x00])]);

        AssertEx.Throws<BinaryFormatException>(
            "UTF-8", () => new BinarySerializer().Deserialize<int>(frame));
    }

    // --- HST-05: the payload mode ----------------------------------------------------------------

    [Fact]
    public void Deserialize_TheReferencesModeRaised_ReadsThePayloadAsFramedAndFails()
    {
        // Written without reference framing: the root's flag reads as the first occurrence of id 0,
        // and the first list's folded count as a back reference to it, which is not a list.
        byte[] frame = new BinarySerializer().Serialize(new SharedLists { A = [1], B = [2] });

        byte[] mutated = Mutate.SetByte(frame, Wire.ModeOffset, 1);

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<SharedLists>(mutated));
    }

    [Fact]
    public void Deserialize_TheReferencesModeCleared_ReadsThePayloadAsUnframedAndFails()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().PreserveReferences().Build());
        byte[] framed = serializer.Serialize(new SharedLists { A = [1], B = [2] });

        byte[] frame = Mutate.SetByte(framed, Wire.ModeOffset, 0);

        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<SharedLists>(frame));
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

    [Fact]
    public void Deserialize_AnOnDiskLengthAboveItsPhaseLimit_ThrowsLimit()
    {
        byte[] body = Wire.Body(Frame());
        byte[] frame = Wire.FrameWith(body, onDiskLength: int.MaxValue);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_AnUncompressedLengthAboveItsPhaseLimit_ThrowsLimit()
    {
        byte[] body = Wire.Body(Frame());
        byte[] frame = Wire.FrameWith(body, services: [Wire.CompressionRecord(1, int.MaxValue)]);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_AnOnDiskLengthShorterThanThePayload_ThrowsFormat()
    {
        byte[] body = Wire.Body(Frame());
        byte[] frame = Wire.Frame(body[..3]);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_AChecksumRecordLongerThanTheFrame_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWith(
            [], services: [Wire.Service(Wire.ChecksumService, critical: true, [1, 0, 0, 0, 0], declaredLength: 200)]);

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(frame));
    }

    // --- HST-07: the checksum ---------------------------------------------------------------------

    [Fact]
    public void Deserialize_AMutatedChecksum_ThrowsIntegrity()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).Build());
        byte[] frame = serializer.Serialize(Sample());

        // The hash is the remainder of the checksum record, after the algorithm id.
        byte[] mutated = Mutate.FlipByte(frame, Wire.ReadHeader(frame).Service(Wire.ChecksumService).BodyOffset + 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(mutated));
    }

    [Fact]
    public void Deserialize_AMutatedPayloadUnderAChecksum_ThrowsIntegrity()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).Build());
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
            BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), key).Build());
        byte[] frame = serializer.Serialize(Sample());

        byte[] mutated = Mutate.FlipByte(frame, frame.Length - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(mutated));
    }

    [Fact]
    public void Deserialize_ATruncatedAuthenticationTag_ThrowsFormatOrIntegrity()
    {
        byte[] key = NewKey();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), key).Build());
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
                .WithEncryption(new Aes256GcmEncryption(), key, "ring")
                .WithChecksum(new Crc32Checksum())
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
                .WithEncryption(new Aes256GcmEncryption(), key, "ring")
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
