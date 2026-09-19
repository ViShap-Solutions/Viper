using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins WF-28, WF-29 and WF-30: the associated-data image covers exactly the §22.7 fields in order,
/// excludes <c>OnDiskLength</c>, and is recomputed by both sides rather than carried on the wire —
/// which is what makes every field it covers unforgeable.
/// </summary>
public class AssociatedDataTests
{
    private static readonly byte[] Key = new byte[32];

    private static BinaryFormatHeaderV1 Sample(int onDiskLength = 33) =>
        new(
            CompressionAlgorithm.Custom, "zip",
            ChecksumAlgorithm.Custom, "sum",
            EncryptionAlgorithm.Custom, "box",
            KeyId: "ring",
            PreserveReferences: true,
            UncompressedLength: 11,
            CompressedLength: 22,
            OnDiskLength: onDiskLength,
            Checksum: [7, 8]);

    [Fact]
    public void BuildAssociatedData_CoversTheDocumentedFieldsInOrder()
    {
        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(1);                            // version
            writer.Write((byte)CompressionAlgorithm.Custom);
            writer.Write("zip");
            writer.Write((byte)ChecksumAlgorithm.Custom);
            writer.Write("sum");
            writer.Write((byte)EncryptionAlgorithm.Custom);
            writer.Write("box");
            writer.Write("ring");                       // key id
            writer.Write(true);                         // preserve references
            writer.Write(11);                           // uncompressed length
            writer.Write(22);                           // compressed length
            writer.Write((byte)2);                      // checksum length
            writer.Write(new byte[] { 7, 8 });
        });

        Assert.Equal(expected, Sample().BuildAssociatedData());
    }

    [Fact]
    public void BuildAssociatedData_AbsentOptionalStrings_AreCoveredAsEmpty()
    {
        var header = new BinaryFormatHeaderV1(
            CompressionAlgorithm.None, null,
            ChecksumAlgorithm.None, null,
            EncryptionAlgorithm.None, null,
            KeyId: null,
            PreserveReferences: false,
            UncompressedLength: 4,
            CompressedLength: 4,
            OnDiskLength: 4,
            Checksum: []);

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(1);
            writer.Write((byte)0); writer.Write(string.Empty);
            writer.Write((byte)0); writer.Write(string.Empty);
            writer.Write((byte)0); writer.Write(string.Empty);
            writer.Write(string.Empty);
            writer.Write(false);
            writer.Write(4);
            writer.Write(4);
            writer.Write((byte)0);
        });

        Assert.Equal(expected, header.BuildAssociatedData());
    }

    [Fact]
    public void BuildAssociatedData_IgnoresOnDiskLength()
    {
        Assert.Equal(Sample(onDiskLength: 33).BuildAssociatedData(), Sample(onDiskLength: 9999).BuildAssociatedData());
    }

    [Fact]
    public void Deserialize_AlteredOnDiskLength_StillFailsBecauseItIsSelfVerifying()
    {
        // Excluded from the image, but not unchecked: a short read cannot satisfy the tag.
        var serializer = Encrypted();
        byte[] frame = serializer.Serialize("associated");
        var header = Wire.ReadHeader(frame);

        byte[] tampered = Mutate.SetInt32(frame, Wire.OnDiskLengthOffset, header.OnDiskLength - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    [Fact]
    public void Serialize_EncryptedFrame_DoesNotCarryTheAssociatedDataImage()
    {
        byte[] frame = Encrypted().Serialize("associated");
        var parsed = Wire.ReadHeader(frame);

        byte[] image = new BinaryFormatHeaderV1(
            (CompressionAlgorithm)parsed.Compression, parsed.CustomCompressionName,
            (ChecksumAlgorithm)parsed.ChecksumAlgorithm, parsed.CustomChecksumName,
            (EncryptionAlgorithm)parsed.Encryption, parsed.CustomEncryptionName,
            parsed.KeyId, parsed.PreserveReferences,
            parsed.UncompressedLength, parsed.CompressedLength, parsed.OnDiskLength,
            parsed.Checksum).BuildAssociatedData();

        Assert.DoesNotContain(image, Windows(frame, image.Length));
    }

    [Fact]
    public void Deserialize_AlteredPreserveReferencesFlag_ThrowsIntegrity()
    {
        var serializer = Encrypted();
        byte[] frame = serializer.Serialize("associated");

        byte[] tampered = Mutate.SetByte(frame, Wire.PreserveReferencesOffset, 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    [Fact]
    public void Deserialize_AlteredKeyId_ThrowsIntegrity()
    {
        // The resolver hands out the same key for any id, so nothing but the tag can notice.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32())
                .WithEncryption(new Aes256Gcm(), _ => Key, keyId: "ring")
                .Build());

        byte[] frame = serializer.Serialize("associated");

        // The key id is the only optional string present: its flag sits at offset 14, its 7-bit
        // length at 15, and its bytes start at 16.
        byte[] tampered = Mutate.FlipByte(frame, 16);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    [Fact]
    public void Deserialize_AlteredChecksumBytesCoveredByTheImage_ThrowsIntegrity()
    {
        var serializer = Encrypted();
        byte[] frame = serializer.Serialize("associated");
        var header = Wire.ReadHeader(frame);

        byte[] tampered = Mutate.FlipByte(frame, header.HeaderLength - header.Checksum.Length);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    /// <summary>
    /// AES-GCM with a checksum, so the frame carries every field §22.7 binds and the tag is what
    /// notices a change to any of them.
    /// </summary>
    private static BinarySerializer Encrypted() =>
        new(BinarySerializerOptions.Configure()
            .WithChecksum(new Crc32())
            .WithEncryption(new Aes256Gcm(), Key)
            .Build());

    /// <summary>Every <paramref name="length"/>-byte window of <paramref name="source"/>.</summary>
    private static IEnumerable<byte[]> Windows(byte[] source, int length)
    {
        for (int start = 0; start + length <= source.Length; start++)
            yield return source[start..(start + length)];
    }
}
