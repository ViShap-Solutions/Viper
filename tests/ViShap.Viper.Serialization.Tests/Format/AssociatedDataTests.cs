using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins WF-28 and WF-30: the associated data authenticated encryption binds is the exact bytes of the
/// header on the wire, from the first byte of the magic to the last byte of <c>onDiskLength</c>, on
/// the way out and on the way in — so no header byte can change without failing the tag.
/// </summary>
public class AssociatedDataTests
{
    private static readonly byte[] Key = new byte[32];

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, "ring")]
    [InlineData(true, true, "ring")]
    public void Serialize_TheAssociatedDataIsTheHeaderBytesExactly(bool checksum, bool compress, string? keyId)
    {
        var cipher = new RecordingCipher();
        var builder = BinarySerializerOptions.Configure()
            .WithEncryption(cipher, Key, keyId)
            .RegisterCustomEncryption(RecordingCipher.RegisteredName, () => cipher);

        if (checksum)
            builder.WithChecksum(new Crc32Checksum());

        if (compress)
            builder.WithCompression(new DeflateCompression());

        var serializer = new BinarySerializer(builder.Build());
        byte[] frame = serializer.Serialize("associated");
        var header = Wire.ReadHeader(frame);

        Assert.Equal(frame[..header.HeaderLength], cipher.SealedWith);

        Assert.Equal("associated", serializer.Deserialize<string>(frame));
        Assert.Equal(frame[..header.HeaderLength], cipher.OpenedWith);
    }

    [Fact]
    public void Serialize_TheOnDiskLengthIsInsideTheAssociatedData()
    {
        var cipher = new RecordingCipher();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(cipher, Key)
                .RegisterCustomEncryption(RecordingCipher.RegisteredName, () => cipher)
                .Build());

        byte[] frame = serializer.Serialize("associated");
        var header = Wire.ReadHeader(frame);

        Assert.Equal(header.HeaderLength, cipher.SealedWith!.Length);
        Assert.Equal(Wire.Varint(header.OnDiskLength), cipher.SealedWith[header.OnDiskLengthOffset..]);
    }

    [Fact]
    public void Serialize_EncryptedFrame_CarriesTheHeaderOnceAndNoSecondImage()
    {
        byte[] frame = Encrypted().Serialize("associated");
        var header = Wire.ReadHeader(frame);
        byte[] headerBytes = frame[..header.HeaderLength];

        Assert.DoesNotContain(headerBytes, Windows(frame[1..], headerBytes.Length));
    }

    [Fact]
    public void Deserialize_AlteredOnDiskLength_ThrowsIntegrity()
    {
        // A shorter declared length is still present in the frame, so only the tag can object.
        var serializer = Encrypted();
        byte[] frame = serializer.Serialize("associated");
        var header = Wire.ReadHeader(frame);

        byte[] shorter =
        [
            .. frame[..header.OnDiskLengthOffset],
            .. Wire.Varint(header.OnDiskLength - 1),
            .. frame[header.HeaderLength..^1]
        ];

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(shorter));
    }

    [Fact]
    public void Deserialize_AlteredPayloadMode_ThrowsIntegrity()
    {
        var serializer = Encrypted();
        byte[] frame = serializer.Serialize("associated");

        byte[] tampered = Mutate.SetByte(frame, Wire.ModeOffset, 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    [Fact]
    public void Deserialize_AlteredKeyId_ThrowsIntegrity()
    {
        // The resolver hands out the same key for any id, so nothing but the tag can notice.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32Checksum())
                .WithEncryption(new Aes256GcmEncryption(), _ => Key, keyId: "ring")
                .Build());

        byte[] frame = serializer.Serialize("associated");
        var record = Wire.ReadHeader(frame).Service(Wire.EncryptionService);

        // The body is the algorithm id, the key id's length plus one, then its bytes. The edit keeps
        // the field valid UTF-8: "ring" becomes "sing".
        byte[] tampered = Mutate.SetByte(frame, record.BodyOffset + 2, (byte)'s');

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    [Fact]
    public void Deserialize_KeyIdEditedIntoInvalidUtf8_ThrowsFormatBeforeTheTagIsChecked()
    {
        // The header is parsed before anything is decrypted. A field that is not a string in the
        // declared encoding therefore fails as malformed input, ahead of the integrity check.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32Checksum())
                .WithEncryption(new Aes256GcmEncryption(), _ => Key, keyId: "ring")
                .Build());

        byte[] frame = serializer.Serialize("associated");
        var record = Wire.ReadHeader(frame).Service(Wire.EncryptionService);

        // 0x8D is a continuation byte with no lead byte in front of it.
        byte[] tampered = Mutate.SetByte(frame, record.BodyOffset + 2, 0x8D);

        AssertEx.Throws<BinaryFormatException>(
            "UTF-8", () => serializer.Deserialize<string>(tampered));
    }

    [Fact]
    public void Deserialize_AlteredChecksumBytes_ThrowsIntegrity()
    {
        var serializer = Encrypted();
        byte[] frame = serializer.Serialize("associated");
        var record = Wire.ReadHeader(frame).Service(Wire.ChecksumService);

        byte[] tampered = Mutate.FlipByte(frame, record.BodyOffset + record.BodyLength - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<string>(tampered));
    }

    /// <summary>
    /// AES-GCM with a checksum, so the frame carries a checksum record and an encryption record and
    /// the tag is what notices a change to any header byte.
    /// </summary>
    private static BinarySerializer Encrypted() =>
        new(BinarySerializerOptions.Configure()
            .WithChecksum(new Crc32Checksum())
            .WithEncryption(new Aes256GcmEncryption(), Key)
            .Build());

    /// <summary>Every <paramref name="length"/>-byte window of <paramref name="source"/>.</summary>
    private static IEnumerable<byte[]> Windows(byte[] source, int length)
    {
        for (int start = 0; start + length <= source.Length; start++)
            yield return source[start..(start + length)];
    }

    /// <summary>A pass-through cipher that records the associated data it is handed in each direction.</summary>
    private sealed class RecordingCipher : IEncryptionAlgorithm
    {
        public const string RegisteredName = "recording";

        public byte[]? SealedWith { get; private set; }

        public byte[]? OpenedWith { get; private set; }

        public EncryptionAlgorithm Kind => EncryptionAlgorithm.Custom;

        public string? CustomName => RegisteredName;

        public bool AuthenticatesAssociatedData => true;

        public int KeySizeInBytes => 32;

        public int GetCiphertextLength(int plaintextLength) => plaintextLength;

        public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData, Span<byte> destination)
        {
            SealedWith = associatedData.ToArray();
            plaintext.CopyTo(destination);
            return plaintext.Length;
        }

        public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData, Span<byte> destination)
        {
            OpenedWith = associatedData.ToArray();
            ciphertext.CopyTo(destination);
            return ciphertext.Length;
        }
    }
}
